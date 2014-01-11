using System;
using System.Web;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;

using GeoAPI.Geometries;

using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

using Nonagon.Geo.Properties;
using System.IO;
using System.Text;

namespace Nonagon.Geo
{
	public static class GeoArea
	{
		private static readonly Dictionary<String, ShapeFileInfo> shapeFileInfoLookup;
		private static readonly String coordCachePath;

		static GeoArea()
		{
			var setting = (Settings)SettingsBase.Synchronized(new Settings());

			if (setting.ShapeFileSetting == null)
			{
				throw new ConfigurationErrorsException(
					"ShapeFileSetting not found from configuration.");
			}

			if (setting.ShapeFileSetting.ShapeFileList == null)
			{
				throw new ConfigurationErrorsException(
					"ShapeFileSetting.ShapeFileList not found from configuration.");
			}

			if (setting.ShapeFileSetting.ShapeFileList.Any(i => i.Key == null))
				throw new ConfigurationErrorsException("ShapeFileInfo must have defined Key.");

			if (setting.ShapeFileSetting.ShapeFileList.Any(i => i.ShapeFilesRootPath == null))
				throw new ConfigurationErrorsException("ShapeFileInfo must have defined ShapeFilesRootPath.");

			shapeFileInfoLookup = setting.ShapeFileSetting.ShapeFileList.ToDictionary(s => s.Key.ToLower());
			coordCachePath = setting.CoordinateCachePath;
		}

		public static List<Coordinate> GetCoordinates(String districtKey)
		{
			var geo = new List<Coordinate>();

			if (districtKey == null)
				return null;

			// In case some provice changed.
			districtKey = OverrideDistrictKeyForSpecial(districtKey);

			var level = 0;
			var distictKeys = districtKey.Split('-');

			if (districtKey != null)
				level = districtKey.Split('-').Length - 1;

			var rootKey = distictKeys[0];

			if (!shapeFileInfoLookup.ContainsKey(rootKey))
				throw new KeyNotFoundException("Key = " + rootKey + " not found from configuration.");

			var levelMap = shapeFileInfoLookup[rootKey].ShapeFileMaps.FirstOrDefault(m => m.Level == level);
			if (levelMap == null)
			{
				throw new KeyNotFoundException(
					"No ShapeFile entry for Level = " + level + " computed from: " + districtKey);
			}

			var rootPath = shapeFileInfoLookup[rootKey].ShapeFilesRootPath;
			var fileName = shapeFileInfoLookup[rootKey].ShapeFileMaps[level].FileName;
			var filePath = rootPath + fileName;
			var cachePath = coordCachePath;

			if (HttpContext.Current != null)
			{
				filePath = HttpContext.Current.Server.MapPath(filePath);

				if(cachePath != null)
					cachePath = HttpContext.Current.Server.MapPath(cachePath + "/" + districtKey);
			}

			// Get coordinate from cache instead if found.
			if (cachePath != null && File.Exists(cachePath))
			{
				try
				{
					using (var streamReader = File.OpenText(cachePath))
					{
						String s = streamReader.ReadToEnd();
						var coordinates = s.Split('-').Select(
							ss => { 
								var p = ss.Split(','); 
								return new Coordinate { 
									X = Double.Parse(p[0]),
									Y = Double.Parse(p[1])
								};
							});

						geo.AddRange(coordinates);
					}

					return geo;
				}
				catch(Exception ex)
				{
					//TODO: Log this exception.
					Console.WriteLine(ex.Message);
				}
			}

			var factory = new GeometryFactory();
			using (var shapeFileDataReader = new ShapefileDataReader(filePath, factory))
			{
				var shapeHeader = shapeFileDataReader.ShapeHeader;
				var bounds = shapeHeader.Bounds;
				var header = shapeFileDataReader.DbaseHeader;

				shapeFileDataReader.Reset();

				while (shapeFileDataReader.Read())
				{
					var keys = new string[header.NumFields];
					var geometry = shapeFileDataReader.Geometry;
					var shapeDisticts = new List<String>();

					for (var i = 0; i < header.NumFields; i++)
					{
						var fieldDescriptor = header.Fields[i];
						keys[i] = fieldDescriptor.Name;

						var fieldValue = shapeFileDataReader.GetValue(i) + "";

						for (var j = 0; j <= level; j++)
						{
							if (fieldDescriptor.Name == "NAME_" + j)
							{
								shapeDisticts.Add(fieldValue.ToLower());
							}
						}
					}

					var shapeDistictKey = String.Join("-", shapeDisticts.ToArray());
					Console.WriteLine(shapeDistictKey);

					if (districtKey == shapeDistictKey)
					{
						var startIndex = 0;

						//HARDCODED: Hack start point to fix wrong shape.
						if (districtKey == "thailand-prachuap khiri khan")
							startIndex = 500;

						for (var i = startIndex; i < geometry.Coordinates.Length; i++)
						{
							geo.Add(geometry.Coordinates[i]);
						}

						break;
					}
				}

				shapeFileDataReader.Close();
				shapeFileDataReader.Dispose();
			}

			// Build cache.
			if (cachePath != null)
			{
				var physicalCachePath = coordCachePath;

				if (HttpContext.Current != null)
					physicalCachePath = HttpContext.Current.Server.MapPath(physicalCachePath);

				if (!Directory.Exists(physicalCachePath))
					Directory.CreateDirectory(physicalCachePath);

				var sb = new StringBuilder();
				foreach (var coord in geo)
				{
					sb.AppendFormat("{0},{1}", coord.X, coord.Y);
					sb.Append("-");
				}

				if (sb.Length > 0)
					sb = sb.Remove(sb.Length - 1, 1);

				var coordCache = sb.ToString();
				File.WriteAllText(cachePath, coordCache);
			}

			return geo;
		}

		private static String OverrideDistrictKeyForSpecial(String districtKey)
		{
			if (districtKey == "thailand-bung kan")
				return "thailand-nong khai-bung kan";

			return districtKey;
		}
	}
}

