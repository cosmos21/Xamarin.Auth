//
//  Copyright 2012-2016, Xamarin Inc.
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
//
using System;
using System.Collections.Generic;
using Android.App;
using Android.Net.Http;
using Android.Webkit;
using Android.OS;
using System.Threading.Tasks;
using Xamarin.Utilities.Android;
using System.Text;
using Java.Interop;

#if !AZURE_MOBILE_SERVICES
namespace Xamarin.Auth
#else
namespace Xamarin.Auth._MobileServices
#endif
{
    [Activity(Label = "Web Authenticator")]
    #if XAMARIN_AUTH_INTERNAL
    internal partial class WebAuthenticatorActivity : global::Android.Accounts.AccountAuthenticatorActivity
    #else
    /// Pull Request - manually added/fixed
    ///		Marshalled NavigationService.GoBack to UI Thread #94
    ///		https://github.com/xamarin/Xamarin.Auth/pull/88
    //public class WebAuthenticatorActivity : Activity
    public partial class WebAuthenticatorActivity : global::Android.Accounts.AccountAuthenticatorActivity
    #endif
    {
        WebView webView;

        internal class State : Java.Lang.Object
        {
            public WebAuthenticator Authenticator;
        }
        internal static readonly ActivityStateRepository<State> StateRepo = new ActivityStateRepository<State>();

        State state;

        class JavascriptInterceptor : Java.Lang.Object
        {
            private WebAuthenticatorActivity _activity;

            public JavascriptInterceptor(WebAuthenticatorActivity activity)
            {
                _activity = activity;
            }

            [Export]
            [JavascriptInterface]
            public void OnReceivedSamlResponse(string base64SamlResponse)
            {
                Console.WriteLine("JavascriptInterface OnReceivedSamlResponse, SAMLResponse={0}", base64SamlResponse);
                _activity.OnSamlResponseReceived2(base64SamlResponse);
            }
        }

        public void OnSamlResponseReceived2(string samlResponse)
        {
            this.RunOnUiThread(delegate {
                //Dictionary<string, string> formParams = new Dictionary<string, string>();
                //formParams.Add("SAMLResponse", samlResponse);

                string url = webView.Url;                
                // Somehow by the time this code is run, webView.Url isn't still (or isn't yet) the redirect url
                // we're looking for, so if we pass that to OnPageLoading the WebRedirectAuthenticator is going to
                // ignore it and not end the authentication process.

                // So we just force the url to be the one it's looking for, because if we've found SAMLResponse
                // then we must be at the stage we need.
                
                // Hacky way of getting the redirecturl we're looking for into this activity class, 
                // because it knows nothing about WebRedirectAuthenticator
                url = this.Intent.GetStringExtra("RedirectUrl");

                //Uri uri = new Uri(webView.Url);
                //if (uri.Query == null)
                {
                    url+= "?SAMLResponse=" + samlResponse;
                }
                //else
                //{
                //    url += "&SAMLResponse=" + samlResponse;
                //}
                //this.state.Authenticator.PostedFormParameters = formParams;
                this.state.Authenticator.OnPageLoading(new Uri(url));

                this.EndProgress();
                this.webView.StopLoading();
            });
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            //
            // Load the state either from a configuration change or from the intent.
            //
            state = LastNonConfigurationInstance as State;
            if (state == null && Intent.HasExtra("StateKey"))
            {
                var stateKey = Intent.GetStringExtra("StateKey");
                state = StateRepo.Remove(stateKey);
            }
            if (state == null)
            {
                Finish();
                return;
            }

            Title = state.Authenticator.Title;

            //
            // Watch for completion
            //
            state.Authenticator.Completed +=
                (s, e) =>
                {
                    SetResult(e.IsAuthenticated ? Result.Ok : Result.Canceled);

                    #region
                    ///-------------------------------------------------------------------------------------------------
                    /// Pull Request - manually added/fixed
                    ///		Added IsAuthenticated check #88
                    ///		https://github.com/xamarin/Xamarin.Auth/pull/88
                    if (e.IsAuthenticated)
                    {
                        if (state.Authenticator.GetAccountResult != null)
                        {
                            var accountResult = state.Authenticator.GetAccountResult(e.Account);

                            Bundle result = new Bundle();
                            result.PutString(global::Android.Accounts.AccountManager.KeyAccountType, accountResult.AccountType);
                            result.PutString(global::Android.Accounts.AccountManager.KeyAccountName, accountResult.Name);
                            result.PutString(global::Android.Accounts.AccountManager.KeyAuthtoken, accountResult.Token);
                            result.PutString(global::Android.Accounts.AccountManager.KeyAccountAuthenticatorResponse, e.Account.Serialize());

                            SetAccountAuthenticatorResult(result);
                        }
                    }
                    ///-------------------------------------------------------------------------------------------------
                    #endregion

                    Finish();
                };

            state.Authenticator.Error +=
            (s, e) =>
            {
                if (!state.Authenticator.ShowErrors)
                    return;

                if (e.Exception != null)
                {
                    this.ShowError("Authentication Error e.Exception = ", e.Exception);
                }
                else
                {
                    this.ShowError("Authentication Error e.Message = ", e.Message);
                }
                BeginLoadingInitialUrl();
            };

            //---------------------------------------------------------------------------------
            //
            // Build the UI
            //
            webView = new WebView(this)
            {
                Id = 42,

            };
            JavascriptInterceptor jsInterceptor = new JavascriptInterceptor(this);
            webView.AddJavascriptInterface(jsInterceptor, "jsInterceptor");

            webView.Settings.UserAgentString = WebViewConfiguration.Android.UserAgent;
            Client web_view_client = new Client(this);  // UserAgent set in the class

            webView.Settings.JavaScriptEnabled = true;
            webView.SetWebViewClient(web_view_client);
            SetContentView(webView);

            //---------------------------------------------------------------------------------

            //
            // Restore the UI state or start over
            //
            if (savedInstanceState != null)
            {
                webView.RestoreState(savedInstanceState);
            }
            else
            {
                if (Intent.GetBooleanExtra("ClearCookies", true))
                    WebAuthenticator.ClearCookies();

                BeginLoadingInitialUrl();
            }

            return;
        }


        #region
        ///-------------------------------------------------------------------------------------------------
        /// Pull Request - manually added/fixed
        ///		Added IsAuthenticated check #88
        ///		https://github.com/xamarin/Xamarin.Auth/pull/88
        protected override void OnResume()
        {
            base.OnResume();
            if (state.Authenticator.AllowCancel && state.Authenticator.IsAuthenticated())
            {
                state.Authenticator.OnCancelled();
            }
        }
        ///-------------------------------------------------------------------------------------------------
        #endregion

        void BeginLoadingInitialUrl()
        {
            state.Authenticator.GetInitialUrlAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                {

                    if (!state.Authenticator.ShowErrors)
                        return;

                    this.ShowError("Authentication Error t.Exception = ", t.Exception);
                }
                else
                {
                    webView.LoadUrl(t.Result.AbsoluteUri);
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        public override void OnBackPressed()
        {
            if (state.Authenticator.AllowCancel)
            {
                state.Authenticator.OnCancelled();
            }
        }

        public override Java.Lang.Object OnRetainNonConfigurationInstance()
        {
            return state;
        }

        protected override void OnSaveInstanceState(Bundle outState)
        {
            base.OnSaveInstanceState(outState);
            webView.SaveState(outState);
        }

        void BeginProgress(string message)
        {
            webView.Enabled = false;
        }

        void EndProgress()
        {
            webView.Enabled = true;
        }

    }
}

