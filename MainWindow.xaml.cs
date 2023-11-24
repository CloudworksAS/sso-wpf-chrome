using Auth0.OidcClient;
using IdentityModel.OidcClient;
using IdentityModel.OidcClient.Browser;
using System.Collections.Generic;
using System.Configuration;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Shell;
using System.Diagnostics;
using System.Net;
using System;

namespace WPFSample
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 

    internal class SystemBrowser : IBrowser
    {
        const string ERROR_MESSAGE = "Error ocurred.";
        const string SUCCESSFUL_AUTHENTICATION_MESSAGE = "You have been successfully authenticated. You can now continue to use desktop application.";
        const string SUCCESSFUL_LOGOUT_MESSAGE = "You have been successfully logged out.";
        private HttpListener _httpListener;
        private void StartSystemBrowser(string startUrl)
        {
            //Process.Start(new ProcessStartInfo(startUrl) { UseShellExecute = true });
            Process.Start("C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe", @startUrl);

        }
        public async Task<BrowserResult> InvokeAsync(BrowserOptions options, CancellationToken cancellationToken = default)
        {
            StartSystemBrowser(options.StartUrl);
            var result = new BrowserResult();

            //abort _httpListener if exists
            _httpListener?.Abort();
            using (_httpListener = new HttpListener())
            {
                var listenUrl = options.EndUrl;

                //HttpListenerContext require uri ends with /
                if (!listenUrl.EndsWith("/"))
                    listenUrl += "/";

                _httpListener.Prefixes.Add(listenUrl);
                _httpListener.Start();
                using (cancellationToken.Register(() =>
                {
                    _httpListener?.Abort();
                }))
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await _httpListener.GetContextAsync();
                    }
                    //if _httpListener is aborted while waiting for response it throws HttpListenerException exception
                    catch (HttpListenerException)
                    {
                        result.ResultType = BrowserResultType.UnknownError;
                        return result;
                    }

                    //set result response url
                    result.Response = context.Request.Url.AbsoluteUri;

                    //generate message displayed in the browser, and set resultType based on request
                    string displayMessage;
                    if (context.Request.QueryString.Get("code") != null)
                    {
                        displayMessage = SUCCESSFUL_AUTHENTICATION_MESSAGE;
                        result.ResultType = BrowserResultType.Success;
                    }
                    else if (options.StartUrl.Contains("/logout") && context.Request.Url.AbsoluteUri == options.EndUrl)
                    {
                        displayMessage = SUCCESSFUL_LOGOUT_MESSAGE;
                        result.ResultType = BrowserResultType.Success;
                    }
                    else
                    {
                        displayMessage = ERROR_MESSAGE;
                        result.ResultType = BrowserResultType.UnknownError;
                    }

                    //return message to be displayed in the browser
                    Byte[] buffer = System.Text.Encoding.UTF8.GetBytes(displayMessage);
                    context.Response.ContentLength64 = buffer.Length;
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context.Response.OutputStream.Close();
                    context.Response.Close();
                    _httpListener.Stop();
                }
            }
            return result;
        }
    }


    public partial class MainWindow : Window
    {
        private Auth0Client client;
        readonly string[] _connectionNames = new string[]
        {
            "Username-Password-Authentication",
            "google-oauth2",
            "twitter",
            "facebook",
            "github",
            "windowslive"
        };

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void LoginButton_OnClick(object sender, RoutedEventArgs e)
        {
            string domain = ConfigurationManager.AppSettings["Auth0:Domain"];
            string clientId = ConfigurationManager.AppSettings["Auth0:ClientId"];

            client = new Auth0Client(new Auth0ClientOptions
            {
                Domain = domain,
                ClientId = clientId,
                Browser = new SystemBrowser(),
                RedirectUri = "http://localhost:8888",
                PostLogoutRedirectUri = "http://localhost:8888/logout"
            });

            var extraParameters = new Dictionary<string, string>();

            if (!string.IsNullOrEmpty(connectionNameComboBox.Text))
                extraParameters.Add("connection", connectionNameComboBox.Text);

            if (!string.IsNullOrEmpty(audienceTextBox.Text))
                extraParameters.Add("audience", audienceTextBox.Text);

            DisplayResult(await client.LoginAsync(extraParameters: extraParameters));
        }

        private void DisplayResult(LoginResult loginResult)
        {
            // Display error
            if (loginResult.IsError)
            {
                resultTextBox.Text = loginResult.Error;
                return;
            }

            logoutButton.Visibility = Visibility.Visible;
            loginButton.Visibility = Visibility.Collapsed;

            // Display result
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Tokens");
            sb.AppendLine("------");
            sb.AppendLine($"id_token: {loginResult.IdentityToken}");
            sb.AppendLine($"access_token: {loginResult.AccessToken}");
            sb.AppendLine($"refresh_token: {loginResult.RefreshToken}");
            sb.AppendLine();

            sb.AppendLine("Claims");
            sb.AppendLine("------");
            foreach (var claim in loginResult.User.Claims)
            {
                sb.AppendLine($"{claim.Type}: {claim.Value}");
            }

            resultTextBox.Text = sb.ToString();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            connectionNameComboBox.ItemsSource = _connectionNames;
        }

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            BrowserResultType browserResult = await client.LogoutAsync();

            if (browserResult != BrowserResultType.Success)
            {
                resultTextBox.Text = browserResult.ToString();
                return;
            }

            logoutButton.Visibility = Visibility.Collapsed;
            loginButton.Visibility = Visibility.Visible;

            audienceTextBox.Text = "";
            resultTextBox.Text = "";
            connectionNameComboBox.ItemsSource = _connectionNames;
        }
    }
}