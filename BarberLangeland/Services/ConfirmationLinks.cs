using Microsoft.AspNetCore.Mvc;

namespace BarberLangeland.Services
{
    public static class ConfirmationLinks
    {
        /// <summary>
        /// The absolute link to <c>/Account/ConfirmEmail</c> for the mail. <c>Site:BaseUrl</c> wins
        /// when set, so the link never depends on the Host header of whoever triggered the mail.
        /// </summary>
        public static string Build(IUrlHelper url, HttpRequest request, SiteOptions site, string userId, string code)
        {
            var path = url.Action("ConfirmEmail", "Account", new { userId, code }) ?? "/Account/ConfirmEmail";

            return string.IsNullOrWhiteSpace(site.BaseUrl)
                ? $"{request.Scheme}://{request.Host}{path}"
                : site.BaseUrl.TrimEnd('/') + path;
        }
    }
}
