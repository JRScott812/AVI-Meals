namespace AVI_Meals.Server.Services;

/// <summary>
/// Validates each request URI (including redirects) against <see cref="OutboundUrlGuard"/>.
/// </summary>
public sealed class SafeOutboundHandler : DelegatingHandler
{
	private const int MaxRedirects = 3;

	/// <summary>
	/// Sends the request after validating the target URL, following a limited number of safe redirects.
	/// </summary>
	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		Uri? currentUri = request.RequestUri;
		OutboundUrlGuard.EnsureAllowed(currentUri);

		for (int redirectCount = 0; ; redirectCount++)
		{
			HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
			if (!IsRedirect(response.StatusCode) || response.Headers.Location is null)
			{
				return response;
			}

			if (redirectCount >= MaxRedirects)
			{
				response.Dispose();
				throw new InvalidOperationException($"Too many redirects while requesting {currentUri}.");
			}

			Uri redirectUri = response.Headers.Location.IsAbsoluteUri
				? response.Headers.Location
				: new Uri(currentUri!, response.Headers.Location);
			response.Dispose();

			OutboundUrlGuard.EnsureAllowed(redirectUri);
			currentUri = redirectUri;
			request.RequestUri = redirectUri;
		}
	}

	private static bool IsRedirect(System.Net.HttpStatusCode statusCode)
	{
		int code = (int)statusCode;
		return code is >= 300 and < 400;
	}
}
