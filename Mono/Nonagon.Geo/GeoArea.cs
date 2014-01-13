using System;
using System.IO;
using System.Text;
using System.Web;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;

using GeoAPI.Geometries;

using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

using Nonagon.Geo;
using Nonagon.Geo.Properties;

namespace Nonagon.Geo
{
	/// <summary>
	/// The Geographical data helper class to work with GADM ShapeFile.
	/// </summary>
	public static class GeoArea
	{
		static readonly Dictionary<String, ShapeFileInfo> shapeFileInfoLookup;
		static readonly String coordCachePath;

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

		/// <summary>
		/// Gets the coordinates from specified district key.
		/// </summary>
		/// <returns>The coordinates. Multiple group of coordinates separated by top level array element.</returns>
		/// <param name="districtKey">District key as country-district-...</param>
		public static IEnumerable<IEnumerable<Coordinate>> GetCoordinates(String districtKey)
		{
			var geo = new List<IEnumerable<Coordinate>>();

			if (districtKey == null)
				return null;

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
						var coordinates = s.Split('|').Select(
							ss => { 
								var areas = ss.Split('-').Select(
									ax => {

										var p = ax.Split(',');
										return new Coordinate { 
											X = Double.Parse(p[0]),
											Y = Double.Parse(p[1])
										};
									});

								return areas;
							});

						if (coordinates.Any(Enumerable.Any))
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
						// Find the duplicate coordinates. It is the polygon loop.
						var endPointLookup = geometry.Coordinates.
						                     GroupBy(k => k.X + "," + k.Y).
						                     Where(g => g.Count() >= 2).
						                     ToLookup(g => g.Key, null);

						String endPoint = null;
						var coords = new List<Coordinate>();

						try
						{
							for (long i = 0; i < geometry.Coordinates.LongLength; i++)
							{
								if(geometry.Coordinates[i] == null)
									continue;

								var key = geometry.Coordinates[i].X + "," +
								          geometry.Coordinates[i].Y;

								coords.Add(geometry.Coordinates[i]);

								if (endPoint == null)
								{
									if (endPointLookup.Contains(key))
										endPoint = key;
								}
								else
								{
									if (endPoint == key)
									{
										endPoint = null;
										geo.Add(coords);
										coords = new List<Coordinate>();
									}
								}
							}
						}
						catch(Exception ex)
						{
							Console.WriteLine(ex.Message);
							throw;
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
				foreach (var coords in geo)
				{
					foreach (var coord in coords)
					{
						sb.AppendFormat("{0},{1}", coord.X, coord.Y);
						sb.Append("-");
					}

					if(sb.Length > 0)
						sb.Remove(sb.Length - 1, 1);

					sb.Append("|");
				}

				if(sb.Length > 0)
					sb.Remove(sb.Length - 1, 1);

				var coordCache = sb.ToString();
				File.WriteAllText(cachePath, coordCache);
			}

			return geo;
		}

		/// <summary>
		/// Gets the union coordinates of specified district key.
		/// </summary>
		/// <returns>The coordinates.</returns>
		/// <param name="districtKeys">Array of district key.</param>
		public static IEnumerable<Coordinate> GetUnionCoordinates(String[] districtKeys)
		{
			// Using Clipper library to do polygon operation.
			var clipper = new Clipper();
			foreach (var dk in districtKeys)
			{
				var polies = new List<List<IntPoint>>();

				var areas = GetCoordinates(dk);
				foreach(var a in areas)
				{
					polies.Add(new List<IntPoint>());

					foreach (var c in a)
						polies[0].Add(new IntPoint(Utils.ToLong(c.X), Utils.ToLong(c.Y)));
				}

				clipper.AddPaths(polies, PolyType.ptSubject, true);
			}

			var solution = new List<List<IntPoint>>();

			clipper.Execute(ClipType.ctUnion, solution, 
				PolyFillType.pftEvenOdd, PolyFillType.pftEvenOdd);

			var coords = new List<Coordinate>();

			foreach (var areas in solution)
			{
				foreach(var p in areas)
				{
					var c = new Coordinate() { X = Utils.ToDouble(p.X), Y = Utils.ToDouble(p.Y) };
					coords.Add(c);
				}
			}

			return coords;
		}

		/// <summary>
		/// Subtract 2 polygon coordinates by top - bottom.
		/// </summary>
		/// <returns>The subtracted coordinates.</returns>
		/// <param name="top">Top polygon coordinates.</param>
		/// <param name="bottom">Bottom polygon coordinates.</param>
		public static IEnumerable<Coordinate> Subtract(IEnumerable<Coordinate> top, IEnumerable<Coordinate> bottom)
		{
			// Using Clipper library to do polygon operation.
			var clipper = new Clipper();

			var topPolies = new List<List<IntPoint>>();
			topPolies.Add(new List<IntPoint>());

			foreach(var c in top)
			{
				topPolies[0].Add(
					new IntPoint(Utils.ToLong(c.X), Utils.ToLong(c.Y)));
			}

			clipper.AddPaths(topPolies, PolyType.ptSubject, true);

			var bottomPolies = new List<List<IntPoint>>();
			bottomPolies.Add(new List<IntPoint>());

			foreach(var c in bottom)
			{
				bottomPolies[0].Add(
					new IntPoint(Utils.ToLong(c.X), Utils.ToLong(c.Y)));
			}

			clipper.AddPaths(bottomPolies, PolyType.ptClip, true);

			var solution = new List<List<IntPoint>>();

			clipper.Execute(ClipType.ctXor, solution, 
				PolyFillType.pftEvenOdd, PolyFillType.pftEvenOdd);

			var coords = new List<Coordinate>();

			foreach (var areas in solution)
			{
				foreach(var p in areas)
				{
					var c = new Coordinate() { X = Utils.ToDouble(p.X), Y = Utils.ToDouble(p.Y) };
					coords.Add(c);
				}
			}

			return coords;
		}
	}
}
