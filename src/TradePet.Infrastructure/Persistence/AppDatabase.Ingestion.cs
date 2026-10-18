using Microsoft.Data.Sqlite;
using TradePet.Core.Domain;

namespace TradePet.Infrastructure.Persistence;

public sealed partial class AppDatabase
{
    public async Task SaveTradesAsync(
        IReadOnlyCollection<TradeRecord> trades,
        CancellationToken cancellationToken = default)
    {
        if (trades.Count == 0)
        {
            return;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO trades(
                    account_key, position_id, symbol, side, opened_at_utc, closed_at_utc,
                    open_server_date, close_server_date, entry_price, exit_price, opening_volume,
                    maximum_volume, remaining_volume, net_pnl, is_complete)
                VALUES (
                    $account, $position, $symbol, $side, $opened, $closed,
                    $openDate, $closeDate, $entry, $exit, $openingVolume,
                    $maximumVolume, $remainingVolume, $netPnl, $complete)
                ON CONFLICT(account_key, position_id) DO UPDATE SET
                    symbol = excluded.symbol,
                    side = excluded.side,
                    opened_at_utc = excluded.opened_at_utc,
                    closed_at_utc = excluded.closed_at_utc,
                    open_server_date = excluded.open_server_date,
                    close_server_date = excluded.close_server_date,
                    entry_price = excluded.entry_price,
                    exit_price = excluded.exit_price,
                    opening_volume = excluded.opening_volume,
                    maximum_volume = excluded.maximum_volume,
                    remaining_volume = excluded.remaining_volume,
                    net_pnl = excluded.net_pnl,
                    is_complete = excluded.is_complete
                WHERE (trades.symbol, trades.side, trades.opened_at_utc, trades.closed_at_utc,
                    trades.open_server_date, trades.close_server_date, trades.entry_price, trades.exit_price,
                    trades.opening_volume, trades.maximum_volume, trades.remaining_volume, trades.net_pnl, trades.is_complete)
                    IS NOT (excluded.symbol, excluded.side, excluded.opened_at_utc, excluded.closed_at_utc,
                    excluded.open_server_date, excluded.close_server_date, excluded.entry_price, excluded.exit_price,
                    excluded.opening_volume, excluded.maximum_volume, excluded.remaining_volume, excluded.net_pnl, excluded.is_complete);
                """;
            command.Parameters.AddWithValue("$account", DBNull.Value);
            command.Parameters.AddWithValue("$position", DBNull.Value);
            command.Parameters.AddWithValue("$symbol", DBNull.Value);
            command.Parameters.AddWithValue("$side", DBNull.Value);
            command.Parameters.AddWithValue("$opened", DBNull.Value);
            command.Parameters.AddWithValue("$closed", DBNull.Value);
            command.Parameters.AddWithValue("$openDate", DBNull.Value);
            command.Parameters.AddWithValue("$closeDate", DBNull.Value);
            command.Parameters.AddWithValue("$entry", DBNull.Value);
            command.Parameters.AddWithValue("$exit", DBNull.Value);
            command.Parameters.AddWithValue("$openingVolume", DBNull.Value);
            command.Parameters.AddWithValue("$maximumVolume", DBNull.Value);
            command.Parameters.AddWithValue("$remainingVolume", DBNull.Value);
            command.Parameters.AddWithValue("$netPnl", DBNull.Value);
            command.Parameters.AddWithValue("$complete", DBNull.Value);
            foreach (var trade in trades)
            {
                command.Parameters["$account"].Value = trade.AccountKey;
                command.Parameters["$position"].Value = trade.PositionId;
                command.Parameters["$symbol"].Value = trade.Symbol;
                command.Parameters["$side"].Value = trade.Side.ToString();
                command.Parameters["$opened"].Value = Format(trade.OpenedAtUtc);
                command.Parameters["$closed"].Value = DbValue(trade.ClosedAtUtc is null ? null : Format(trade.ClosedAtUtc.Value));
                command.Parameters["$openDate"].Value = Format(trade.OpenServerDate);
                command.Parameters["$closeDate"].Value = DbValue(trade.CloseServerDate is null ? null : Format(trade.CloseServerDate.Value));
                command.Parameters["$entry"].Value = trade.EntryPrice;
                command.Parameters["$exit"].Value = DbValue(trade.ExitPrice);
                command.Parameters["$openingVolume"].Value = trade.OpeningVolume;
                command.Parameters["$maximumVolume"].Value = trade.MaximumVolume;
                command.Parameters["$remainingVolume"].Value = trade.RemainingVolume;
                command.Parameters["$netPnl"].Value = trade.NetPnl;
                command.Parameters["$complete"].Value = trade.IsComplete ? 1 : 0;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveDealBatchAsync(
        string accountKey,
        IReadOnlyCollection<DealRecord> deals,
        IReadOnlyCollection<AccountCashFlow> cashFlows,
        HistorySyncState? historyState = null,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<long, long>? mt4PositionAliases = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountKey);
        if (cashFlows.Any(item => item.AccountKey != accountKey) ||
            historyState is not null && historyState.AccountKey != accountKey)
        {
            throw new ArgumentException("Every batch item must belong to the requested account.", nameof(accountKey));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        if (mt4PositionAliases is { Count: > 0 })
            await ReconcileMt4PositionsAsync(connection, transaction, accountKey, mt4PositionAliases, deals, cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO deals(
                    account_key, ticket, order_ticket, position_id, symbol, side, entry_kind,
                    volume, price, profit, commission, swap, fee, occurred_at_utc)
                VALUES (
                    $account, $ticket, $order, $position, $symbol, $side, $entry,
                    $volume, $price, $profit, $commission, $swap, $fee, $occurred)
                ON CONFLICT(account_key, ticket) DO UPDATE SET
                    order_ticket = excluded.order_ticket,
                    position_id = excluded.position_id,
                    symbol = excluded.symbol,
                    side = excluded.side,
                    entry_kind = excluded.entry_kind,
                    volume = excluded.volume,
                    price = excluded.price,
                    profit = excluded.profit,
                    commission = excluded.commission,
                    swap = excluded.swap,
                    fee = excluded.fee,
                    occurred_at_utc = excluded.occurred_at_utc
                WHERE (deals.order_ticket, deals.position_id, deals.symbol, deals.side, deals.entry_kind,
                    deals.volume, deals.price, deals.profit, deals.commission, deals.swap, deals.fee, deals.occurred_at_utc)
                    IS NOT (excluded.order_ticket, excluded.position_id, excluded.symbol, excluded.side, excluded.entry_kind,
                    excluded.volume, excluded.price, excluded.profit, excluded.commission, excluded.swap, excluded.fee, excluded.occurred_at_utc);
                """;
            command.Parameters.AddWithValue("$account", DBNull.Value);
            command.Parameters.AddWithValue("$ticket", DBNull.Value);
            command.Parameters.AddWithValue("$order", DBNull.Value);
            command.Parameters.AddWithValue("$position", DBNull.Value);
            command.Parameters.AddWithValue("$symbol", DBNull.Value);
            command.Parameters.AddWithValue("$side", DBNull.Value);
            command.Parameters.AddWithValue("$entry", DBNull.Value);
            command.Parameters.AddWithValue("$volume", DBNull.Value);
            command.Parameters.AddWithValue("$price", DBNull.Value);
            command.Parameters.AddWithValue("$profit", DBNull.Value);
            command.Parameters.AddWithValue("$commission", DBNull.Value);
            command.Parameters.AddWithValue("$swap", DBNull.Value);
            command.Parameters.AddWithValue("$fee", DBNull.Value);
            command.Parameters.AddWithValue("$occurred", DBNull.Value);
            foreach (var deal in deals)
            {
                command.Parameters["$account"].Value = accountKey;
                command.Parameters["$ticket"].Value = deal.Ticket;
                command.Parameters["$order"].Value = deal.OrderTicket;
                command.Parameters["$position"].Value = deal.PositionId;
                command.Parameters["$symbol"].Value = deal.Symbol;
                command.Parameters["$side"].Value = deal.Side.ToString();
                command.Parameters["$entry"].Value = deal.EntryKind.ToString();
                command.Parameters["$volume"].Value = deal.Volume;
                command.Parameters["$price"].Value = deal.Price;
                command.Parameters["$profit"].Value = deal.Profit;
                command.Parameters["$commission"].Value = deal.Commission;
                command.Parameters["$swap"].Value = deal.Swap;
                command.Parameters["$fee"].Value = deal.Fee;
                command.Parameters["$occurred"].Value = Format(deal.OccurredAtUtc);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO account_cash_flows(account_key, ticket, type, amount, occurred_at_utc)
                VALUES ($account, $ticket, $type, $amount, $occurred)
                ON CONFLICT(account_key, ticket) DO UPDATE SET
                    type = excluded.type,
                    amount = excluded.amount,
                    occurred_at_utc = excluded.occurred_at_utc
                WHERE (account_cash_flows.type, account_cash_flows.amount, account_cash_flows.occurred_at_utc)
                    IS NOT (excluded.type, excluded.amount, excluded.occurred_at_utc);
                """;
            command.Parameters.AddWithValue("$account", DBNull.Value);
            command.Parameters.AddWithValue("$ticket", DBNull.Value);
            command.Parameters.AddWithValue("$type", DBNull.Value);
            command.Parameters.AddWithValue("$amount", DBNull.Value);
            command.Parameters.AddWithValue("$occurred", DBNull.Value);
            foreach (var cashFlow in cashFlows)
            {
                command.Parameters["$account"].Value = accountKey;
                command.Parameters["$ticket"].Value = cashFlow.Ticket;
                command.Parameters["$type"].Value = cashFlow.Type;
                command.Parameters["$amount"].Value = cashFlow.Amount;
                command.Parameters["$occurred"].Value = Format(cashFlow.OccurredAtUtc);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        if (historyState is not null)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO history_sync_state(account_key, range_year, is_complete, deal_count, updated_at_utc)
                VALUES ($account, $year, $complete, $count, $updated)
                ON CONFLICT(account_key, range_year) DO UPDATE SET
                    is_complete = excluded.is_complete,
                    deal_count = excluded.deal_count,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$account", historyState.AccountKey);
            command.Parameters.AddWithValue("$year", historyState.RangeYear);
            command.Parameters.AddWithValue("$complete", historyState.IsComplete ? 1 : 0);
            command.Parameters.AddWithValue("$count", historyState.DealCount);
            command.Parameters.AddWithValue("$updated", Format(historyState.UpdatedAtUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
