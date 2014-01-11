using System;
using System.Configuration;

namespace Nonagon.Geo.Properties
{
	public sealed class Settings : ApplicationSettingsBase
	{
		[ApplicationScopedSetting]
		public ShapeFileSetting ShapeFileSetting
		{
			get
			{
				return (ShapeFileSetting)this["ShapeFileSetting"];
			}
			set
			{
				this["ShapeFileSetting"] = value;
			}
		}

		[ApplicationScopedSetting]
		public String CoordinateCachePath
		{
			get
			{
				return (String)this["CoordinateCachePath"];
			}
			set
			{
				this["CoordinateCachePath"] = value;
			}
		}
	}
}

