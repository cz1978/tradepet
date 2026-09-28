using Microsoft.Data.Sqlite;
using TradePet.Core.Domain;

namespace TradePet.Infrastructure.Persistence;

public sealed partial class AppDatabase
{
    private static async Task ReconcileMt4PositionsAsync(SqliteConnection connection, SqliteTransaction transaction,
        string account, IReadOnlyDictionary<long, long> aliases, IReadOnlyCollection<DealRecord> deals, CancellationToken token)
    {
        if (!account.StartsWith("MT4:", StringComparison.Ordinal) || aliases.Any(p => p.Key <= p.Value || p.Value <= 0 ||
            !deals.Any(d => d.PositionId == p.Value && d.EntryKind == DealEntryKind.In)))
            throw new InvalidDataException("Invalid MT4 position linkage.");
        foreach (var (child, parent) in aliases.OrderBy(p => p.Key))
        {
            async Task<int> Execute(string sql)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                command.Parameters.AddWithValue("$account", account);
                command.Parameters.AddWithValue("$child", child);
                command.Parameters.AddWithValue("$parent", parent);
                return await command.ExecuteNonQueryAsync(token);
            }
            var changed = await Execute("""
                INSERT INTO mt4_position_links(account_key,ticket,position_id) VALUES($account,$child,$parent)
                ON CONFLICT(account_key,ticket) DO UPDATE SET position_id=excluded.position_id
                WHERE mt4_position_links.position_id<>excluded.position_id;
                """);
            if (changed > 0)
            {
                // Keep the original documents/revisions as an audit trail and copy all written
                // review fields into the surviving position. Never overwrite a user's notes.
                var old = await LoadTradeReviewDocumentAsync(connection, new(account, child), token, transaction);
                var current = await LoadTradeReviewDocumentAsync(connection, new(account, parent), token, transaction);
                if (old is not null || current is not null)
                {
                    var basis = current ?? old!;
                    string Merge(string existing, string? previous) => string.IsNullOrWhiteSpace(previous) ? existing :
                        $"{existing}\n[MT4 原票号 #{child}] {previous}".Trim();
                    var merged = basis with
                    {
                        TradeKey = new(account, parent), Status = ReviewCompletionStatus.NeedsReview,
                        EntryReason = Merge(current?.EntryReason ?? "", old?.EntryReason),
                        ExitReason = Merge(current?.ExitReason ?? "", old?.ExitReason),
                        DidWell = Merge(current?.DidWell ?? "", old?.DidWell),
                        ToImprove = Merge(current?.ToImprove ?? "", old?.ToImprove),
                        NextAction = Merge(current?.NextAction ?? "", old?.NextAction),
                        Summary = Merge(current?.Summary ?? "", old?.Summary),
                        Emotion = Merge(current?.Emotion ?? "", old?.Emotion),
                        MarketCondition = Merge(current?.MarketCondition ?? "", old?.MarketCondition),
                        Revision = (current?.Revision ?? 0) + 1, UpdatedAtUtc = DateTimeOffset.UtcNow,
                        ReviewedSourceVersion = null, ReviewedRuleVersion = null, ReviewedAtUtc = null,
                    };
                    await using var save = connection.CreateCommand();
                    save.Transaction = transaction;
                    save.CommandText = """
                        INSERT OR REPLACE INTO trade_review_documents(account_key,position_id,status,revision,
                            source_version,rule_version,updated_at_utc,payload_json)
                        VALUES($account,$position,$status,$revision,$source,$rule,$updated,$payload);
                        """;
                    save.Parameters.AddWithValue("$account", account);
                    save.Parameters.AddWithValue("$position", parent);
                    save.Parameters.AddWithValue("$status", merged.Status.ToString());
                    save.Parameters.AddWithValue("$revision", merged.Revision);
                    save.Parameters.AddWithValue("$source", merged.SourceVersion);
                    save.Parameters.AddWithValue("$rule", merged.RuleVersion);
                    save.Parameters.AddWithValue("$updated", Format(merged.UpdatedAtUtc));
                    save.Parameters.AddWithValue("$payload", Serialize(merged));
                    await save.ExecuteNonQueryAsync(token);
                    await SaveRevisionAsync(connection, transaction, "trade", account, parent.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        merged.Revision, merged, merged.UpdatedAtUtc, token);
                }
                await Execute("""
                    INSERT INTO trade_review_metadata
                    SELECT account_key,$parent,plan_id,compliance_status,strategy,setup,tags_json,user_edited,updated_at_utc
                    FROM trade_review_metadata WHERE account_key=$account AND position_id=$child
                    ON CONFLICT(account_key,position_id) DO UPDATE SET plan_id=excluded.plan_id,
                        compliance_status=excluded.compliance_status,strategy=excluded.strategy,setup=excluded.setup,
                        tags_json=excluded.tags_json,user_edited=excluded.user_edited,updated_at_utc=excluded.updated_at_utc
                    WHERE trade_review_metadata.user_edited=0 AND excluded.user_edited=1;
                    INSERT OR IGNORE INTO attachment_links
                    SELECT attachment_id,account_key,owner_kind,CAST($parent AS TEXT),title,evidence_json,event_reference
                    FROM attachment_links WHERE account_key=$account AND owner_kind='trade' AND owner_id=CAST($child AS TEXT);
                    INSERT OR IGNORE INTO behavior_trade_links
                    SELECT account_key,occurrence_id,$parent,role FROM behavior_trade_links
                    WHERE account_key=$account AND position_id=$child;
                    DELETE FROM behavior_trade_links WHERE account_key=$account AND position_id=$child;
                    DELETE FROM trade_excursions WHERE account_key=$account AND position_id=$parent;
                    """);
                await BumpReviewVersionAsync(connection, transaction, account, "metadata", token);
            }
            // Only obsolete derived rows are removed; all source ticket IDs survive on exit deals.
            await Execute("""
                DELETE FROM deals WHERE account_key=$account AND ticket=$child*2 AND entry_kind='In';
                UPDATE deals SET position_id=$parent WHERE account_key=$account AND position_id=$child;
                DELETE FROM trades WHERE account_key=$account AND position_id=$child;
                """);
        }
    }
}
