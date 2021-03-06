using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Media;
using AcManager.Controls.UserControls.Web;
using AcTools.Utils.Helpers;
using CefSharp;
using FirstFloor.ModernUI.Helpers;
using JetBrains.Annotations;

namespace AcManager.Controls.UserControls.Cef {
    internal class RequestHandler : IRequestHandler {
        [CanBeNull]
        internal string UserAgent { get; set; }

        [CanBeNull]
        internal ICustomStyleProvider StyleProvider { get; set; }

        public bool OnBeforeBrowse(IWebBrowser browserControl, IBrowser browser, IFrame frame, IRequest request, bool isRedirect) {
            // if (request.TransitionType.HasFlag(TransitionType.ForwardBack)) return true;
            return RequestsFiltering.ShouldBeBlocked(request.Url);
        }

        public bool OnOpenUrlFromTab(IWebBrowser browserControl, IBrowser browser, IFrame frame, string targetUrl, WindowOpenDisposition targetDisposition,
                bool userGesture) {
            return false;
        }

        public bool OnCertificateError(IWebBrowser browserControl, IBrowser browser, CefErrorCode errorCode, string requestUrl, ISslInfo sslInfo,
                IRequestCallback callback) {
            if (!callback.IsDisposed) {
                callback.Dispose();
            }

            Logging.Warning(requestUrl);
            return false;
        }

        public void OnPluginCrashed(IWebBrowser browserControl, IBrowser browser, string pluginPath) {
            Logging.Warning(pluginPath);
        }

        public CefReturnValue OnBeforeResourceLoad(IWebBrowser browserControl, IBrowser browser, IFrame frame, IRequest request, IRequestCallback callback) {
            if (RequestsFiltering.ShouldBeBlocked(request.Url)) {
                if (!callback.IsDisposed) {
                    callback.Dispose();
                }

                return CefReturnValue.Cancel;
            }

            if (UserAgent != null) {
                var headers = request.Headers;
                headers[@"User-Agent"] = UserAgent;
                request.Headers = headers;
            }

            return CefReturnValue.Continue;
        }

        public bool GetAuthCredentials(IWebBrowser browserControl, IBrowser browser, IFrame frame, bool isProxy, string host, int port, string realm,
                string scheme, IAuthCallback callback) {
            return true;
        }

        public bool OnSelectClientCertificate(IWebBrowser browserControl, IBrowser browser, bool isProxy, string host, int port,
                X509Certificate2Collection certificates,
                ISelectClientCertificateCallback callback) {
            return true;
        }

        public void OnRenderProcessTerminated(IWebBrowser browserControl, IBrowser browser, CefTerminationStatus status) {
            Logging.Warning(status);
        }

        public bool OnQuotaRequest(IWebBrowser browserControl, IBrowser browser, string originUrl, long newSize, IRequestCallback callback) {
            if (!callback.IsDisposed) {
                callback.Dispose();
            }

            return true;
        }

        public void OnResourceRedirect(IWebBrowser browserControl, IBrowser browser, IFrame frame, IRequest request, IResponse response, ref string newUrl) { }

        public bool OnProtocolExecution(IWebBrowser browserControl, IBrowser browser, string url) {
            return url.StartsWith(@"mailto") || url.StartsWith(@"acmanager");
        }

        public void OnRenderViewReady(IWebBrowser browserControl, IBrowser browser) { }

        public bool OnResourceResponse(IWebBrowser browserControl, IBrowser browser, IFrame frame, IRequest request, IResponse response) {
            return false;
        }

        private static string GetColor(string key) {
            return Application.Current.TryFindResource(key) is SolidColorBrush b
                    ? $@"background:{b.Color.ToHexString()}!important;opacity:{b.Opacity}!important;" : "";
        }

        public event EventHandler<WebInjectEventArgs> Inject;

        private readonly string _windowColor = (Application.Current.TryFindResource(@"WindowBackgroundColor") as Color?)?.ToHexString();
        private readonly string _scrollThumbColor = GetColor(@"ScrollBarThumb");
        private readonly string _scrollThumbHoverColor = GetColor(@"ScrollBarThumbHover");
        private readonly string _scrollThumbDraggingColor = GetColor(@"ScrollBarThumbDragging");

        public IResponseFilter GetResourceResponseFilter(IWebBrowser browserControl, IBrowser browser, IFrame frame, IRequest request, IResponse response) {
            if (response.MimeType == @"text/html") {
                var css = StyleProvider?.GetStyle(request.Url, browserControl is CefSharp.Wpf.ChromiumWebBrowser);
                var inject = new WebInjectEventArgs(request.Url);
                Inject?.Invoke(this, inject);
                return new ReplaceResponseFilter(inject.Replacements.Append(ReplaceResponseFilter.CreateCustomCss(inject.ToInject.JoinToString(), $@"
::-webkit-scrollbar {{ width: 8px!important; height: 8px!important; }}
::-webkit-scrollbar-track {{ box-shadow: none!important; border-radius: 0!important; background: {_windowColor}!important; opacity: 0!important; }}
::-webkit-scrollbar-corner {{ background: {_windowColor}!important; }}
::-webkit-scrollbar-thumb {{ border: none!important; box-shadow: none!important; border-radius: 0!important; {_scrollThumbColor} }}
::-webkit-scrollbar-thumb:hover {{ {_scrollThumbHoverColor} }}
::-webkit-scrollbar-thumb:active {{ {_scrollThumbDraggingColor} }}", css)));
            }

            return null;
        }

        public void OnResourceLoadComplete(IWebBrowser browserControl, IBrowser browser, IFrame frame, IRequest request, IResponse response,
                UrlRequestStatus status, long receivedContentLength) { }

        private class ReplaceResponseFilter : StreamReplacement, IResponseFilter {
            public static KeyValuePair<string, string> CreateCustomCss(string prefix, params string[] css) {
                return new KeyValuePair<string, string>(@"</head>", $@"{prefix}<style>{css.NonNull().JoinToString('\n')}</style></head>");
            }

            public ReplaceResponseFilter(IEnumerable<KeyValuePair<string, string>> replacements) : base(replacements) {}

            bool IResponseFilter.InitFilter() {
                return true;
            }

            FilterStatus IResponseFilter.Filter(Stream dataIn, out long dataInRead, Stream dataOut, out long dataOutWritten) {
                return Filter(dataIn, out dataInRead, dataOut, out dataOutWritten) ? FilterStatus.Done : FilterStatus.NeedMoreData;
            }

            void IDisposable.Dispose() { }
        }
    }
}