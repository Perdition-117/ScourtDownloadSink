using System.Net;
using Microsoft.AspNetCore.Http.Extensions;

namespace ScourtDownloadSink;

class Program {
	private const int ListenPort = 49553;
	private const string HostEn = "com3d2-shop-dl1-us.s-court.me";
	private const string HostJp = "com3d2-shop-dl1.s-court.me";
	private const string Host = HostEn;

	static async Task Main() {
		Console.Title = "ScourtDownloadSink";

		var client = new HttpClient();
		client.DefaultRequestHeaders.Add("User-Agent", "COM3D2UP");
		client.Timeout = TimeSpan.FromMinutes(5);

		var listener = new HttpListener();
		listener.Prefixes.Add($"http://localhost:{ListenPort}/");
		try {
			listener.Start();
		} catch (HttpListenerException e) {
			Console.Error.WriteLine(e.Message);
			return;
		}

		Console.WriteLine($"Listening on port {ListenPort}");

		while (true) {
			var context = listener.GetContext();

			var downloadUri = GetDownloadUri(context.Request.Url);
			Console.WriteLine();
			Console.WriteLine($"Retrieving shop item data...");
			using var response = await client.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead);
			if (response.IsSuccessStatusCode) {
				if (response.Content.Headers.ContentDisposition?.FileName != null) {
					var fileName = response.Content.Headers.ContentDisposition.FileName.Trim('"');
					var fileSize = response.Content.Headers.ContentLength;
					Console.WriteLine($"Downloading {fileName} ({fileSize / 1e6:N0} MB)...");
					using var stream = await response.Content.ReadAsStreamAsync();
					using var fs = new FileStream(fileName, FileMode.Create);

					var bufferSize = 81920;
					var totalBytesRead = 0;
					var buffer = new Memory<byte>(new byte[bufferSize]);
					int bytesRead;
					while ((bytesRead = await stream.ReadAsync(buffer)) != 0) {
						await fs.WriteAsync(buffer);
						totalBytesRead += bytesRead;
						Console.Write($"\rDownloaded {totalBytesRead / 1e6,5:N0} / {fileSize / 1e6,5:N0} MB ({((float)totalBytesRead / fileSize) * 100,3:N0}%)");
					}

					Console.WriteLine();
					Console.WriteLine($"Successfully downloaded {fileName}.");
				} else {
					Console.Write("Failed to download.");
					var body = await response.Content.ReadAsStringAsync();
					switch (body) {
						case "-7":
							Console.WriteLine(" Error reading file name. Invalid server product information.");
							break;
						default:
							Console.WriteLine($" (error code {body})");
							break;
					}
				}
			} else {
				Console.WriteLine($"Failed to download. ({response.StatusCode} {response.ReasonPhrase})");
			}

			context.Response.StatusCode = (int)HttpStatusCode.NoContent;
			context.Response.StatusDescription = "No Content";
			context.Response.ContentLength64 = 0;
			context.Response.OutputStream.Close();
		}
	}

	private static Uri GetDownloadUri(Uri requestUri) {
		string? itemId = null;
		string? ott = null;
		string? itoken = null;
		string cmd = "0";

		var segments = requestUri.Segments;
		for (var i = 0; i < segments.Length - 1; i++) {
			var keySegment = segments[i].TrimEnd('/');
			var valueSegment = segments[i + 1].TrimEnd('/');
			switch (keySegment) {
				case "itemid":
					itemId = valueSegment;
					break;
				case "ott":
					ott = valueSegment;
					break;
				case "itoken":
					itoken = valueSegment;
					break;
				case "cmd":
					cmd = valueSegment;
					break;
			}
		}

		var query = new QueryBuilder(new Dictionary<string, string> {
			["itemid"] = itemId,
			["ott"] = ott,
			["itoken"] = itoken,
			["ver"] = "2",
			["cmd"] = "2",
		});

		var uriBuilder = new UriBuilder {
			Host = Host,
			Path = "api/download.php",
			Query = query.ToString(),
		};

		return uriBuilder.Uri;
	}
}
