using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Helpers;
using Instances;

namespace FlatOut2MusicManager
{
	class Program
	{
		static void Main(string[] args)
		{
			//args = new[] {@"C:\vault\flatout2_mod", @"Z:\BAK_WTF\music for driving" };
			if (args.Length != 2)
			{
				Console.WriteLine("Arguments: <FlatOut2 dir> <Music dir>");
				return;
			}

			var gameDir = new DirectoryInfo(args[0]);
			if (!gameDir.Exists)
			{
				Console.WriteLine($"FlatOut directory does not exist: [{gameDir.FullName}]");
				return;
			}

			var musicDir = new DirectoryInfo(args[1]);
			if (!musicDir.Exists)
			{
				Console.WriteLine($"Music directory does not exist: [{musicDir.FullName}]");
				return;
			}

			var bfsArchive = gameDir.EnumerateFiles().Single(x => x.Name == ArchiveName);
			if (!bfsArchive.Exists)
			{
				Console.WriteLine($"Archive does not exist: [{bfsArchive.FullName}]");
				return;
			}

			var archiver = gameDir.EnumerateFiles().Single(x => x.Name == "bfs2pack_con.exe");
			if (!archiver.Exists)
			{
				Console.WriteLine($"Archiver does not exist: [{archiver.FullName}]");
				return;
			}

			var unpackDir = UnpackIfNeeded(gameDir, archiver, bfsArchive);
			var playlistDir = unpackDir.EnumerateDirectories().Single(x => x.Name == "music");
			var playlist = GetPlaylistAndBackupIfNeeded(playlistDir);

			var userMusic = GetUserMusic(musicDir);
			var destinationDir = GetEmptyDestinationDir(unpackDir);

			var userTracks = userMusic.Select((x,i) => ConvertAndMove(x, i, userMusic.Count, destinationDir))
				.Where(x => x != null)
				.ToList();

			var allTracks = DefaultTracks
				.Concat(userTracks)
				.ToList();
			
			WritePlaylist(allTracks, playlist);
			Console.WriteLine($"Saved playlist");
			
			PackWithBackup(gameDir, unpackDir, archiver, bfsArchive);
			Console.WriteLine($"Done! Now go and smash'em with new {userTracks.Count} songs!");
		}

		private static void PackWithBackup(DirectoryInfo gameDir, DirectoryInfo unpackDir, FileInfo archiver, FileInfo bfsArchive)
		{
			var bak = gameDir.EnumerateFiles().SingleOrDefault(x => x.Name == $"{bfsArchive.Name}.bak");
			if (bak == null)
			{
				var newFile = bfsArchive.CopyTo($"{bfsArchive.FullName}.bak");
				Console.WriteLine($"Created backup [{newFile.FullName}]");
			}
			Console.WriteLine("Packing... Please wait, this takes a while...");
			var args = $"a {unpackDir.Name} {bfsArchive.Name}";
			var info = new ProcessStartInfo
			{
				FileName = archiver.FullName,
				Arguments = args,
				WorkingDirectory = gameDir.FullName,
				RedirectStandardOutput = true,
				UseShellExecute = false
			};

			var p = new Process
			{
				StartInfo = info
			};
			p.OutputDataReceived  += OnPackPrint;
			p.Start();
			p.BeginOutputReadLine();
			p.WaitForExit();
		}

		private static void OnPackPrint(object sender, DataReceivedEventArgs e)
		{
			if (e.Data == null)
			{
				return;
			}

			var value = e.Data.Split('[').LastOrDefault()?.Split('%').FirstOrDefault();
			if (string.IsNullOrWhiteSpace(value) || !int.TryParse(value, out var percent))
			{
				return;
			}

			var roughPercent = percent / 10 * 10;
			if (roughPercent > PackProgress)
			{
				PackProgress = roughPercent;
				Console.WriteLine($"{PackProgress}%");
			}
			else if (percent == 99 && PackProgress < 100)
			{
				Console.WriteLine($"99%, almost there... DO NOT INTERRUPT, WORKING!");
				PackProgress = 100;
			}
		}

		private static void OnUnpackPrint(object sender, DataReceivedEventArgs e)
		{
			if (e.Data == null)
			{
				return;
			}

			var value = e.Data.Split('[').LastOrDefault()?.Split('%').FirstOrDefault();
			if (string.IsNullOrWhiteSpace(value) || !int.TryParse(value, out var percent))
			{
				return;
			}

			var roughPercent = percent / 10 * 10;
			if (roughPercent > UnpackProgress)
			{
				UnpackProgress = roughPercent;
				Console.WriteLine($"{UnpackProgress}%");
			}
			else if (percent == 99 && UnpackProgress < 100)
			{
				Console.WriteLine($"99%, almost there...");
				UnpackProgress = 100;
			}
		}
		
		private static int PackProgress = 0;
		private static int UnpackProgress = 0;

		private static void WritePlaylist(IReadOnlyList<Track> tracks, FileInfo playlist)
		{
			using var sw = playlist.CreateText();
			sw.WriteLine("Loop	= {");
			sw.WriteLine($"	Count = {tracks.Count},");
			for(var i=0; i<tracks.Count; i++)
			{
				var track = tracks[i];
				sw.WriteLine($"	[{i+1}] = {{");
				sw.WriteLine($"		File = \"{track.File.Replace('\\', '/')}\",");
				sw.WriteLine($"		Artist = \"{track.Artist}\",");
				sw.WriteLine($"		Song = \"{track.Song}\",");
				sw.WriteLine($"		StartPos = 0,");
				sw.WriteLine($"	}},");
			}
			sw.WriteLine("}");
		}

		private static Track ConvertAndMove(FileInfo input, int i, int total, DirectoryInfo destinationDir)
		{
			try
			{
				var nameNoExt = Path.GetFileNameWithoutExtension(input.Name);
				var fileName = $"{i:D3}.ogg";
				var output = new FileInfo(Path.Combine(destinationDir.FullName, fileName));

				FFMpegArguments.FromFileInput(input.FullName).
					OutputToFile(output.FullName, true, options =>
					{
						options.WithAudioCodec(AudioCodec.LibVorbis)
							.ForceFormat("ogg")
							.DisableChannel(Channel.Video);
					}).ProcessSynchronously();

				var result = MakeTrack(input, destinationDir, nameNoExt, fileName);
				Console.WriteLine($"{i + 1}/{total} Converted [{input.FullName}] => [{output.FullName}]. Displayed as [{result.Artist} - {result.Song}]");
				return result;
			}
			catch (Exception e)
			{
				Console.WriteLine($"{i + 1}/{total} FAILED [{input.FullName}], skipping... Exception: {e}");
				return null;
			}
		}

		private static Track MakeTrack(FileInfo fileInfo, DirectoryInfo destinationDir, string nameNoExt, string fileName)
		{
			var relativePath = Path.Combine(destinationDir.Parent.Name, destinationDir.Name, fileName);
			using (Instance instance = PrepareInstance(fileInfo.FullName, 2147483647))
			{
				instance.BlockUntilFinished();
				var json = JsonDocument.Parse(string.Join(string.Empty, instance.OutputData));
				var formatElement = json.RootElement.EnumerateObject()
					.FirstOrDefault(x => x.Name == "format")
					.Value;
				var tagsElement = formatElement.EnumerateObject()
					.FirstOrDefault(x => x.Name == "tags")
					.Value;
				if (tagsElement.ValueKind != JsonValueKind.Object)
				{
					return new Track()
					{
						Artist = string.Empty,
						Song = FilterString(nameNoExt),
						File = relativePath
					};
				}

				var tags = tagsElement.EnumerateObject().ToList();
				var artist = GetValueOrDefault(tags.FirstOrDefault(x => x.Name == "artist").Value, string.Empty);
				var title = GetValueOrDefault(tags.FirstOrDefault(x => x.Name == "title").Value, nameNoExt);
				
				return new Track()
				{
					Artist = FilterString(artist),
					Song = FilterString(title),
					File = relativePath
				};
			}
		}

		private static string FilterString(string value)
		{
			return FilterRegex.Replace(value, "");
		}

		private static string GetValueOrDefault(JsonElement jsonElement, string defaultValue)
		{
			if (jsonElement.ValueKind != JsonValueKind.String)
			{
				return defaultValue;
			}

			var jsonString = jsonElement.GetString();
			
			if (string.IsNullOrWhiteSpace(jsonString))
			{
				return defaultValue;
			}

			return jsonString;
		}
		
		private static Instance PrepareInstance(string filePath, int outputCapacity)
		{
			FFProbeHelper.RootExceptionCheck();
			FFProbeHelper.VerifyFFProbeExists();
			string arguments = "-loglevel error -print_format json -show_format -sexagesimal -show_streams \"" + filePath + "\"";
			return new Instance(FFMpegOptions.Options.FFProbeBinary(), arguments)
			{
				DataBufferCapacity = outputCapacity
			};
		}

		private static DirectoryInfo GetEmptyDestinationDir(DirectoryInfo dir)
		{
			var destinationDir = new DirectoryInfo(Path.Combine(dir.FullName, DestinationDirName));
			if (!destinationDir.Exists)
			{
				destinationDir.Create();
			}

			foreach (var file in destinationDir.EnumerateFiles())
			{
				file.Delete();
			}

			return destinationDir;
		}

		private static FileInfo GetPlaylistAndBackupIfNeeded(DirectoryInfo playlistDir)
		{
			var playlist = playlistDir.EnumerateFiles().SingleOrDefault(x => x.Name == "playlist_ingame.bed");
			var bak = playlistDir.EnumerateFiles().SingleOrDefault(x => x.Name == $"{playlist.Name}.bak");
			if (bak == null)
			{
				var newFile = playlist.CopyTo($"{playlist.FullName}.bak");
				Console.WriteLine($"Created backup [{newFile.FullName}]");
			}

			return playlist;
		}

		static DirectoryInfo UnpackIfNeeded(DirectoryInfo dir, FileInfo archiver, FileInfo bfsArchive)
		{
			var existing = dir.EnumerateDirectories().SingleOrDefault(x => x.Name == UnpackDirName);
			if (existing != null)
			{
				Console.WriteLine("Already unpacked");
				return existing;
			}

			Console.WriteLine("Unpacking...");
			
			var info = new ProcessStartInfo
			{
				FileName = archiver.FullName,
				Arguments = $"x {bfsArchive.Name}",
				WorkingDirectory = dir.FullName,
				RedirectStandardOutput = true,
				UseShellExecute = false
			};
			var p = new Process
			{
				StartInfo = info
			};
			p.OutputDataReceived  += OnUnpackPrint;
			p.Start();
			p.BeginOutputReadLine();
			p.WaitForExit();

			return dir.EnumerateDirectories().Single(x => x.Name == UnpackDirName);
		}

		static IReadOnlyList<FileInfo> GetUserMusic(DirectoryInfo musicDir)
		{
			return musicDir.EnumerateFiles("*.*", SearchOption.AllDirectories)
				.Where(x => MusicExtensions.Contains(x.Extension.ToLowerInvariant()))
				.ToList();
		}

		private const string ArchiveName = "fo2a.bfs";
		private const string UnpackDirName = "data";
		private const string DestinationDirName = "songs_custom";
		private static Regex FilterRegex = new(@"[^a-zA-Z0-9\s\,\.]", RegexOptions.Compiled);


		private static HashSet<string> MusicExtensions = new()
		{
			".mp3", ".ogg", ".wav", ".wma", ".aac", ".ac3", ".flac"
		};

		private static List<Track> DefaultTracks = new()
		{
			new Track
			{
				File = "data/songs1/01.ogg",
				Artist = "The Chelsea Smiles",
				Song = "Nowhere Ride",

			},
			new Track
			{
				File = "data/songs1/19.ogg",
				Artist = "Rob Zombie",
				Song = "Demon Speeding",

			},
			new Track
			{
				File = "data/songs1/02.ogg",
				Artist = "Alkaline Trio",
				Song = "Fall Victim",

			},
			new Track
			{
				File = "data/songs1/08.ogg",
				Artist = "Yellowcard",
				Song = "Rough Landing Holly",

			},
			new Track
			{
				File = "data/songs1/09.ogg",
				Artist = "Yellowcard",
				Song = "Breathing",

			},
			new Track
			{
				File = "data/songs1/10.ogg",
				Artist = "Zebrahead",
				Song = "Lobotomy for Dummies",

			},
			new Track
			{
				File = "data/songs1/11.ogg",
				Artist = "Rise Against",
				Song = "Give It All",

			},
			new Track
			{
				File = "data/songs1/13.ogg",
				Artist = "Papa Roach",
				Song = "Not Listening",

			},
			new Track
			{
				File = "data/songs1/15.ogg",
				Artist = "Fall Out Boy",
				Song = "7 Minutes In Heaven",

			},
			new Track
			{
				File = "data/songs1/16.ogg",
				Artist = "Supergrass",
				Song = "Richard III",

			},
			new Track
			{
				File = "data/songs1/17.ogg",
				Artist = "Nickelback",
				Song = "Flat On the Floor",

			},
			new Track
			{
				File = "data/songs1/18.ogg",
				Artist = "Rob Zombie",
				Song = "Feel So Numb",

			},
			new Track
			{
				File = "data/songs1/20.ogg",
				Artist = "Wolfmother",
				Song = "Pyramid",

			},
			new Track
			{
				File = "data/songs1/21.ogg",
				Artist = "Wolfmother",
				Song = "Dimension",

			},
			new Track
			{
				File = "data/songs1/22.ogg",
				Artist = "Fall Out Boy",
				Song = "Snitches, and Talkers Get...",

			},
			new Track
			{
				File = "data/songs2/35.ogg",
				Artist = "Audioslave",
				Song = "Man Or Animal",

			},
			new Track
			{
				File = "data/songs2/36.ogg",
				Artist = "Audioslave",
				Song = "Your Time Has Come",

			},
			new Track
			{
				File = "data/songs1/14.ogg",
				Artist = "Underoath",
				Song = "Reinventing Your Exit",

			},
		};
	}

	public class Track
    {
        public string File { get; set; }
        public string Artist { get; set; }
        public string Song { get; set; }
    }
}