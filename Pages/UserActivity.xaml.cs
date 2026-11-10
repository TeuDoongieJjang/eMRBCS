using eMRBCS.Global;
using eMRBCS.Models;
using eMRBCS.Services;

namespace eMRBCS.Pages;

public partial class UserActivity : ContentPage
{
    DBService dBService;
    List<User_activity> user_activities;


    public UserActivity()
    {
        InitializeComponent();
        OnLoadUserActivity();

    }

    private async void OnLoadUserActivity()
    {
        dBService = new DBService();
        user_activities = new List<User_activity>();
        user_activities = await dBService.GetAllUserActivity(LoginUser.role);
        if (user_activities != null)
        {
            string logDetails;
            List<string> userlogs = new List<string>();
            foreach (User_activity userAct in user_activities)
            {
                DateTime logdate = DateTime.Parse(userAct.log_datetime);
                string userlogdate = logdate.ToString("MM-dd hh:mm tt");
                logDetails = userlogdate + " | " + userAct.username + " access " + userAct.form_use + " and " + userAct.activity + " with " + userAct.status + " status. RefID:" + userAct.id.ToString("00000");
                userlogs.Add(logDetails);

            }
            lstLogs.ItemsSource = userlogs;


            lstLogs.ItemTemplate = new DataTemplate(() =>
            {

                var label = new Label();
                label.Margin = new Thickness(5, 0, 5, 0);
                label.SetBinding(Label.TextProperty, ".");


                var stackLayout = new StackLayout();
                stackLayout.Children.Add(label);


                var doubleTapRecognizer = new TapGestureRecognizer
                {
                    NumberOfTapsRequired = 2
                };


                doubleTapRecognizer.Tapped += (s, e) =>
                {
                    foreach (var child in stackLayout)
                    {
                        if (child is Label lblchild)
                        {
                            var item = lblchild.BindingContext;
                            lstLogs.SelectedItem = item;
                            OnShowMore();
                        }

                    }

                };

                stackLayout.GestureRecognizers.Add(doubleTapRecognizer);

                return new ViewCell { View = stackLayout };
            });


        }
    }

    private async void TapImgBack(object sender, TappedEventArgs e)
    {
        await Navigation.PopOnceAsync();
    }

    private async void TapShowLog(object sender, TappedEventArgs e)
    {
        OnShowMore();
    }

    private async void OnShowMore()
    {
        if (lstLogs.SelectedItem != null)
        {
            string strlog = lstLogs.SelectedItem.ToString();
            string refid = strlog.Substring(strlog.Length - 11);
            string[] _refid = refid.Split(":");
            int userActID = int.Parse(_refid[1]);
            User_activity _selectedActivity = user_activities.Where(u => u.id == userActID).FirstOrDefault();
            string _logtoDisplay = "";
            if (_selectedActivity != null)
            {
                _logtoDisplay = "Date/Time: " + _selectedActivity.log_datetime + "\n" +
                                "Username: " + _selectedActivity.username + "\n" +
                                "Form: " + _selectedActivity.form_use + "\n" +
                                "Activity: " + _selectedActivity.activity + "\n\n" +
                                "Status: " + _selectedActivity.status + "\n" +
                                "Unbilled Consumers: " + _selectedActivity.unbilledconsumer.ToString() + "\n" +
                                "Billed Consumers: " + _selectedActivity.billedconsumer.ToString() + "\n" +
                                "Collected Consumers: " + _selectedActivity.collectedconsumer.ToString() + "\n";


            }

            await DisplayAlertAsync("Log Details", _logtoDisplay, "Ok");
        }

    }

    private void ClickShowMore(object sender, EventArgs e)
    {
        OnShowMore();
    }
}