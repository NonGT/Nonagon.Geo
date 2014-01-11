using System;
using System.Xml.Serialization;

namespace Nonagon.Geo
{
	public sealed class ShapeFileMap
	{
		[XmlAttribute]
		public Int16 Level { get; set; }

		[XmlAttribute]
		public String FileName { get; set; }
	}
}

