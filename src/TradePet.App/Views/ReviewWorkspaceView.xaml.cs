namespace TradePet.App.Views;

public partial class ReviewWorkspaceView : System.Windows.Controls.UserControl
{
    public ReviewWorkspaceView() => InitializeComponent();

    public void SelectGuideTab(int index) => WorkspaceTabs.SelectedIndex =
        Math.Clamp(index, 0, WorkspaceTabs.Items.Count - 1);
}
