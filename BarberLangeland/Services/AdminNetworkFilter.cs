using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace BarberLangeland.Services
{
    /// <summary>
    /// Settings for <see cref="AdminNetworkOnlyAttribute"/>. In Azure set the app setting
    /// <c>Admin__AllowedIps</c> to a comma separated list of addresses or CIDR ranges, for example
    /// <c>203.0.113.7,198.51.100.0/24,2001:db8::/48</c>. Changing it needs no deploy.
    /// </summary>
    public class AdminAccessOptions
    {
        public string AllowedIps { get; set; } = string.Empty;
    }

    public static class AdminAccessPolicy
    {
        /// <summary>
        /// True when the address matches an entry in the list. An empty list, an unknown address
        /// or an unparsable entry never grants access, so a misconfiguration fails closed.
        /// </summary>
        public static bool IsAllowed(string? allowedIps, IPAddress? address)
        {
            if (address == null || string.IsNullOrWhiteSpace(allowedIps))
            {
                return false;
            }

            var client = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

            foreach (var entry in allowedIps.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                if (entry.Contains('/'))
                {
                    if (IPNetwork.TryParse(entry, out var network) && network.Contains(client))
                    {
                        return true;
                    }
                }
                else if (IPAddress.TryParse(entry, out var single))
                {
                    var allowed = single.IsIPv4MappedToIPv6 ? single.MapToIPv4() : single;
                    if (allowed.Equals(client))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Refuses the request unless it comes from an allowed address. Runs before [Authorize]
    /// (Order) so nothing about the admin area is revealed to other networks.
    /// </summary>
    public class AdminNetworkFilter : IAuthorizationFilter
    {
        private readonly IOptionsMonitor<AdminAccessOptions> _options;
        private readonly ILogger<AdminNetworkFilter> _logger;

        public AdminNetworkFilter(IOptionsMonitor<AdminAccessOptions> options, ILogger<AdminNetworkFilter> logger)
        {
            _options = options;
            _logger = logger;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var address = context.HttpContext.Connection.RemoteIpAddress;

            if (AdminAccessPolicy.IsAllowed(_options.CurrentValue.AllowedIps, address))
            {
                return;
            }

            _logger.LogWarning("Admin area refused for {Address} on {Path}.",
                address, context.HttpContext.Request.Path);

            // The address is shown so the owner can copy it into Admin__AllowedIps after their
            // home IP changes.
            context.Result = new ContentResult
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ContentType = "text/plain; charset=utf-8",
                Content = "Administration er kun tilgængelig fra godkendte IP-adresser.\n" +
                          $"Din IP-adresse er {address}."
            };
        }
    }

    /// <summary>Restricts a controller to the addresses in <see cref="AdminAccessOptions"/>.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class AdminNetworkOnlyAttribute : TypeFilterAttribute
    {
        public AdminNetworkOnlyAttribute() : base(typeof(AdminNetworkFilter))
        {
            Order = -100;
        }
    }
}
