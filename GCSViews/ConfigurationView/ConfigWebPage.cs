using MissionPlanner.Controls;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Drawing;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews.ConfigurationView
{
    /// <summary>
    /// A setup page that is a website (the AM32 configurator, the TitanLRS flasher, EdgeTX Buddy, an LRS backpack) in a
    /// WebView2, loaded the first time the page is opened. A page that sets <see cref="UnreachableMessage"/> first checks
    /// the site answers, and says so instead, with a Retry, when it doesn't.
    /// </summary>
    public abstract class ConfigWebPage : MyUserControl, IActivate
    {
        private const int ReachTimeoutMs = 2000;

        private readonly WebView2 webView;
        private readonly Panel unreachable;
        private readonly Label unreachableText;
        private bool introduced;
        private bool navigated;
        private bool loading;

        protected ConfigWebPage()
        {
            webView = new WebView2
            {
                Dock = DockStyle.Fill,
                Name = "webView",
                TabIndex = 0
            };

            // over the browser while the site can't be reached
            unreachableText = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.BottomCenter,
                Font = new Font("Segoe UI", 11F),
                Padding = new Padding(40, 0, 40, 16)
            };
            var retry = new MyButton {Text = "Retry", Size = new Size(110, 28), Anchor = AnchorStyles.Top};
            retry.Click += (s, e) => Activate();
            var retryRow = new Panel {Dock = DockStyle.Bottom, Height = 0};
            retryRow.Controls.Add(retry);
            retryRow.Resize += (s, e) => retry.Location = new Point((retryRow.Width - retry.Width) / 2, 0);
            unreachable = new Panel {Dock = DockStyle.Fill, Visible = false};
            // the text fills down to the button, which sits under the middle of the page
            unreachable.Resize += (s, e) => retryRow.Height = Math.Max(0, unreachable.Height / 2 - 20);
            unreachable.Controls.Add(unreachableText);
            unreachable.Controls.Add(retryRow);

            SuspendLayout();
            Controls.Add(unreachable);
            Controls.Add(webView);
            Name = GetType().Name;
            Size = new Size(800, 600);
            ResumeLayout(false);
        }

        /// <summary>The site the page shows.</summary>
        protected abstract string Url { get; }

        /// <summary>
        /// What to say when the site doesn't answer; null to leave a site that can't be reached to the browser.
        /// </summary>
        protected virtual string UnreachableMessage => null;

        /// <summary>Runs before the site first loads, for a page with something to say first.</summary>
        protected virtual void BeforeFirstLoad()
        {
        }

        public async void Activate()
        {
            if (navigated || loading)
                return;
            loading = true;

            try
            {
                if (!introduced)
                {
                    introduced = true;
                    BeforeFirstLoad();
                }

                if (UnreachableMessage != null && !await Reachable(new Uri(Url)))
                {
                    ShowUnreachable();
                    return;
                }

                unreachable.Visible = false;
                await webView.EnsureCoreWebView2Async(null);
                if (UnreachableMessage != null)
                {
                    webView.CoreWebView2.NavigationCompleted -= NavigationCompleted;
                    webView.CoreWebView2.NavigationCompleted += NavigationCompleted;
                }

                webView.CoreWebView2.Navigate(Url);
                navigated = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("WebView2 error: " + ex.Message);
            }
            finally
            {
                loading = false;
            }
        }

        private void NavigationCompleted(object sender,
            Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            // it answered the check but the page didn't load: the next Activate or Retry tries again
            if (!e.IsSuccess)
            {
                navigated = false;
                ShowUnreachable();
            }
        }

        private void ShowUnreachable()
        {
            unreachableText.Text = UnreachableMessage + "\n\nNo answer from " + new Uri(Url).Host + ".";
            unreachable.Visible = true;
            unreachable.BringToFront();
        }

        /// <summary>Whether something answers at the site's host and port, within a couple of seconds.</summary>
        private static async Task<bool> Reachable(Uri uri)
        {
            using (var client = new TcpClient())
            {
                try
                {
                    var connect = client.ConnectAsync(uri.Host, uri.Port);
                    // a connect given up on fails when the client closes: seen here, not as an unobserved exception
                    _ = connect.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                    if (await Task.WhenAny(connect, Task.Delay(ReachTimeoutMs)) != connect)
                        return false;
                    await connect;
                    return client.Connected;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
