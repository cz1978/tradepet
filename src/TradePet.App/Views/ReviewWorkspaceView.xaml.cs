namespace TradePet.App.Views;

public partial class ReviewWorkspaceView : System.Windows.Controls.UserControl
{
    public ReviewWorkspaceView() => InitializeComponent();

    private void WorkspaceTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.Source == WorkspaceTabs && WorkspaceTabs.SelectedIndex == 2 &&
            DataContext is ViewModels.Review.ReviewWorkspaceViewModel viewModel)
            viewModel.RefreshSavedReviewsCommand.Execute(null);
    }

    private void ArchiveLists_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.Source != ArchiveLists || ArchiveLists.SelectedIndex != 1 ||
            DataContext is not ViewModels.Review.ReviewWorkspaceViewModel viewModel) return;
        viewModel.RefreshSavedReviewsCommand.Execute(null);
        OpenSelectedSavedReview(viewModel);
    }

    private void SavedArchiveReviews_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.Source == SavedArchiveReviews && ArchiveLists.SelectedIndex == 1 &&
            DataContext is ViewModels.Review.ReviewWorkspaceViewModel viewModel)
            OpenSelectedSavedReview(viewModel);
    }

    private static void OpenSelectedSavedReview(ViewModels.Review.ReviewWorkspaceViewModel viewModel)
    {
        if (viewModel.SelectedSavedReview is { } row && viewModel.SelectedPositionId != row.PositionId)
            row.OpenCommand.Execute(null);
    }

    public void SelectGuideTab(int index) => WorkspaceTabs.SelectedIndex =
        Math.Clamp(index, 0, WorkspaceTabs.Items.Count - 1);
}
