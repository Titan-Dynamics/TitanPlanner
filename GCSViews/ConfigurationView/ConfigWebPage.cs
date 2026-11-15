using MissionPlanner.Controls;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews.ConfigurationView
{
    /// <summary>
    /// A setup page that is a website (the AM32 configurator, the TitanLRS flasher) in a WebView2, loaded the first
    /// time the page is opened.
    /// </summary>
    public abstract class ConfigWebPage : MyUserControl, IActivate
    {
        private readonly WebView2 webView;
        private bool navigated;

        protected ConfigWebPage()
        {
            webView = new WebView2
            {
                Dock = DockStyle.Fill,
                Name = "webView",
                TabIndex = 0
            };

            SuspendLayout();
            Controls.Add(webView);
            Name = GetType().Name;
            Size = new System.Drawing.Size(800, 600);
            ResumeLayout(false);
        }

        /// <summary>The site the page shows.</summary>
        protected abstract string Url { get; }

        /// <summary>Runs before the site first loads, for a page with something to say first.</summary>
        protected virtual void BeforeFirstLoad()
        {
        }

        public async void Activate()
        {
            if (navigated)
                return;
            navigated = true;

            BeforeFirstLoad();

            try
            {
                await webView.EnsureCoreWebView2Async(null);
                webView.CoreWebView2.Navigate(Url);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("WebView2 error: " + ex.Message);
            }
        }
    }
}
