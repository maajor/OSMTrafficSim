using System;
using UnityEngine;

namespace OSMTrafficSim
{
    public static class Conversion
    {
        
        private const int EarthRadius = 6378137; //no seams with globe example
        private const double OriginShift = 2 * Math.PI * EarthRadius / 2;
        
        private static void LatLonToMeters(Vector2 v, out double out_x, out double out_y)
        {
            LatLonToMeters(v.y, v.x, out out_x, out out_y);
        }

        private static void LatLonToMeters(float lat, float lon, out double out_x, out double out_y)
        {
            out_x = lon * OriginShift / 180;
            out_y = Math.Log(Math.Tan((90.0 + lat) * Math.PI / 360)) / (Math.PI / 180);
            out_y = out_y * OriginShift / 180;
        }

        public static Vector2 GeoToWorldPosition(Vector2 longlat, float scale = 1)
        {
            double x1, y1;
            LatLonToMeters(longlat, out x1, out y1);
            return new Vector2((float)x1, (float)y1);
        }

        public static Vector2 GeoToWorldPosition(Vector2 longlat, Vector2 refPoint, float scale = 1)
        {
            double x1, x2, y1, y2;
            LatLonToMeters(longlat, out x1, out y1);
            LatLonToMeters(refPoint, out x2, out y2);
            x1 -= x2;
            y1 -= y2;
            return new Vector2((float)x1, (float)y1);
        }
    }
}
