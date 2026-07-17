using System.Text;

namespace AVI_Meals.Server.Services;

/// <summary>
/// Fetches allowlisted upstream HTML/JSON with a response size cap.
/// </summary>
internal sealed class DiningHttpFetcher(HttpClient httpClient)
{
	private const long MaxResponseBytes = 2 * 1024 * 1024;

	public async Task<string> GetHtmlAsync(string url, CancellationToken cancellationToken)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || !OutboundUrlGuard.IsAllowed(uri))
		{
			throw new InvalidOperationException($"Outbound request blocked for untrusted URL: {url}");
		}

		using HttpResponseMessage response = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
		_ = response.EnsureSuccessStatusCode();
		return await ReadContentAsStringAsync(response, cancellationToken).ConfigureAwait(false)
			?? throw new InvalidOperationException($"Upstream response for {uri} exceeded the size limit.");
	}

	public async Task<string?> TryGetJsonAsync(string url, CancellationToken cancellationToken)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || !OutboundUrlGuard.IsAllowed(uri))
		{
			return null;
		}

		using HttpResponseMessage response = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			return null;
		}

		string? payload = await ReadContentAsStringAsync(response, cancellationToken).ConfigureAwait(false);
		return payload is null
			|| payload.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
			|| payload.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
			? null
			: payload;
	}

	private static async Task<string?> ReadContentAsStringAsync(
		HttpResponseMessage response,
		CancellationToken cancellationToken)
	{
		if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaxResponseBytes)
		{
			return null;
		}

		await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
		using MemoryStream buffer = new();
		byte[] chunk = new byte[8192];
		long totalBytes = 0;
		while (true)
		{
			int read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken).ConfigureAwait(false);
			if (read == 0)
			{
				break;
			}

			totalBytes += read;
			if (totalBytes > MaxResponseBytes)
			{
				return null;
			}

			await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
		}

		return Encoding.UTF8.GetString(buffer.ToArray());
	}
}
