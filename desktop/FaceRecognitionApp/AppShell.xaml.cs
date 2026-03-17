namespace FaceRecognitionApp;

public partial class AppShell : Shell
{
    public AppShell(MainPage mainPage, HistoryPage historyPage, AddPersonPage addPersonPage)
    {
        InitializeComponent();

        var tabBar = new TabBar();

        tabBar.Items.Add(new ShellContent
        {
            Title = "rozpoznawanie",
            Content = mainPage,
        });

        tabBar.Items.Add(new ShellContent
        {
            Title = "historia",
            Content = historyPage,
        });

        tabBar.Items.Add(new ShellContent
        {
            Title = "dodaj osobe",
            Content = addPersonPage,
        });

        Items.Add(tabBar);
    }
}
