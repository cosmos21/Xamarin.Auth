using System;
using Foundation;
using WebKit;

namespace Xamarin.Auth
{
    // COSMOS

    internal partial class WebAuthenticatorController
    {

        IWKScriptMessageHandler _wKScriptMessageHandler = null;

        private WKUserContentController GetUserContentController()
        {
            string jsToInject = this.authenticator.InjectedJavascriptForPage(null);

            if (jsToInject == null)
            {
                return null;
            }

            var userContentController = new WKUserContentController();

            if (_wKScriptMessageHandler == null)
            {
                _wKScriptMessageHandler = new SamlWKWebViewJavascriptMessageHandler(this);
            }
            userContentController.AddScriptMessageHandler(_wKScriptMessageHandler, "jsInterceptor");

            WKUserScript userScript = new WKUserScript(new NSString(jsToInject), WKUserScriptInjectionTime.AtDocumentEnd, isForMainFrameOnly: true);
            userContentController.AddUserScript(userScript);

            return userContentController;
        }

        public void OnSamlResponseReceived(string samlResponse)
        {
            WebRedirectAuthenticator webRedirectAuthenticator = this.authenticator as WebRedirectAuthenticator;
            if (webRedirectAuthenticator != null)
            {
                string uri = webRedirectAuthenticator.GetRedirectUrl().ToString();

                uri += "?SAMLResponse=" + samlResponse;

                webRedirectAuthenticator.OnPageLoading(new Uri(uri));
                this.DismissViewControllerAsync(true);
            }
        }
    }


    internal class SamlWKWebViewJavascriptMessageHandler : WKScriptMessageHandler
    {
        WebAuthenticatorController _controller;

        public SamlWKWebViewJavascriptMessageHandler(WebAuthenticatorController authenticatorController)
        {
            _controller = authenticatorController;
        }

        public override void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
        {
            string response = message.Body.ToString();
            _controller.OnSamlResponseReceived(response);

        }
    }
    // ------
}
