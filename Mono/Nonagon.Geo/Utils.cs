using System;

namespace Nonagon.Geo
{
	static class Utils
	{
		const Double scale = 1024.0 * 1024.0;

		public static Int64 ToLong(Double value)
		{
			if(value < 0)
			{
				if(value < Double.MinValue)
					throw new ArgumentOutOfRangeException();

				return (Int64)(value * scale - 0.5);
			}

			if (value > Double.MaxValue)
				throw new ArgumentOutOfRangeException();

			return (Int64)(value * scale + 0.5);
		}

		public static Double ToDouble(Int64 value)
		{
			return (value - 0.5) / scale;
		}
	}
}

