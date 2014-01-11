using System;
using System.Collections.ObjectModel;

namespace Nonagon.Geo
{
	public sealed class ShapeFileInfo
	{
		public String Key { get; set; }
		public String ShapeFilesRootPath { get; set; }
		public Collection<ShapeFileMap> ShapeFileMaps { get; set; }
	}
}

