namespace AVI_Meals.Server.Data;

/// <summary>
/// Resolves the Postgres connection string from standard ASP.NET or Heroku/Neon env vars.
/// </summary>
public static class DatabaseConnection
{
	public static string? Resolve(IConfiguration configuration)
	{
		string? configured = configuration.GetConnectionString("DefaultConnection");
		if (!string.IsNullOrWhiteSpace(configured))
		{
			return Normalize(configured);
		}

		string? databaseUrl = configuration["DATABASE_URL"];
		return string.IsNullOrWhiteSpace(databaseUrl) ? null : Normalize(databaseUrl);
	}

	public static bool IsConfigured(IConfiguration configuration) =>
		!string.IsNullOrWhiteSpace(Resolve(configuration));

	internal static string Normalize(string connectionString)
	{
		string trimmed = connectionString.Trim();
		if (!trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
			&& !trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
		{
			return trimmed;
		}

		Uri uri = new(trimmed);
		string userInfo = uri.UserInfo;
		string username = Uri.UnescapeDataString(userInfo.Split(':', 2)[0]);
		string password = userInfo.Contains(':', StringComparison.Ordinal)
			? Uri.UnescapeDataString(userInfo.Split(':', 2)[1])
			: string.Empty;
		string database = uri.AbsolutePath.Trim('/');
		string host = uri.Host;
		int port = uri.IsDefaultPort ? 5432 : uri.Port;

		Dictionary<string, string> query = ParseQuery(uri.Query);
		string sslMode = query.TryGetValue("sslmode", out string? sslModeValue)
			? MapSslMode(sslModeValue)
			: "Require";
		bool channelBinding = query.TryGetValue("channel_binding", out string? binding)
			&& string.Equals(binding, "require", StringComparison.OrdinalIgnoreCase);

		string normalized =
			$"Host={host};Port={port};Database={database};Username={username};Password={password};SSL Mode={sslMode};Trust Server Certificate=true";
		if (channelBinding)
		{
			normalized += ";Channel Binding=Require";
		}

		return normalized;
	}

	private static Dictionary<string, string> ParseQuery(string query)
	{
		Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
		string trimmed = query.TrimStart('?');
		if (string.IsNullOrWhiteSpace(trimmed))
		{
			return values;
		}

		foreach (string pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			string[] parts = pair.Split('=', 2);
			string key = Uri.UnescapeDataString(parts[0]);
			string value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
			values[key] = value;
		}

		return values;
	}

	private static string MapSslMode(string sslMode) => sslMode.ToLowerInvariant() switch
	{
		"disable" => "Disable",
		"allow" => "Allow",
		"prefer" => "Prefer",
		"require" => "Require",
		"verify-ca" => "VerifyCA",
		"verify-full" => "VerifyFull",
		_ => "Require"
	};
}
