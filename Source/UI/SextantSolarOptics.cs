using System;
using PlayWay.Water;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Mean sea level is MainWater.transform.y, not camera.worldY or
    /// instantaneous wave WaterLevel. Only the PIP-only measuring Sun changes.</summary>
    internal sealed class SextantSolarOptics
    {
        private Camera cachedCamera;
        private WaterCamera waterCamera;
        public bool Ready { get; private set; }
        public double EyeHeightM { get; private set; }
        public double HorizonAltitudeDeg { get; private set; }
        public double DiameterDeg { get; private set; }
        public double ExpectedHsDeg { get; private set; }
        public Vector3 Direction { get; private set; }
        public DateTime Utc { get; private set; }
        public int ConfiguredFrame { get; private set; }
        public double ObserverLatDeg { get; private set; }
        public double ObserverLonDeg { get; private set; }

        public void Refresh(Camera camera, bool canSight, DateTime utc, double lat, double lon)
        {
            Ready = false;
            Direction = Vector3.zero;
            if (!canSight || camera == null || !Sh5SunAlmanac.Loaded)
                return;
            if (camera != cachedCamera)
            {
                cachedCamera = camera;
                waterCamera = camera.GetComponent<WaterCamera>();
            }
            Water water = waterCamera != null ? waterCamera.MainWater : null;
            if (water == null)
                return;
            double eye = camera.transform.position.y - water.transform.position.y;
            float curvature = Shader.GetGlobalFloat("_EarthCurvatureCompensation");
            if (curvature != 0f && curvature != 1f)
                return;
            double horizon;
            if (!SolarSeaHorizon.TryAltitude(eye, SextantPipClip.FarPlane, curvature == 1f, out horizon))
                return;
            double diameter = SunDiscScale.AngularDiameterDeg(
                AstroMath.ToJulianDay(utc) + SunEphemerisCalibration.DeltaTSeconds / 86400);
            double alt, az;
            if (!Sh5SolarNavigation.TryApparentCentre(utc, lat, lon, eye, horizon, diameter, out alt, out az))
                return;
            double radians = Math.PI / 180;
            double cos = Math.Cos(alt * radians);
            Direction = new Vector3((float)(cos * Math.Sin(az * radians)),
                (float)Math.Sin(alt * radians), (float)(cos * Math.Cos(az * radians)));
            Utc = utc;
            ConfiguredFrame = Time.frameCount;
            ObserverLatDeg = lat;
            ObserverLonDeg = lon;
            EyeHeightM = eye;
            HorizonAltitudeDeg = horizon;
            DiameterDeg = diameter;
            ExpectedHsDeg = alt - diameter / 2 - horizon;
            Ready = true;
        }

        public SunDiscSighting Sighting(bool rendererReady)
        {
            return new SunDiscSighting
            {
                TableRequested = true, TableCompatible = Ready && rendererReady,
                ObservedDiameterDeg = Ready && rendererReady ? DiameterDeg : 0,
                Limb = SunSightLimb.LowerLimb, EyeHeightM = EyeHeightM,
                HorizonAltitudeDeg = HorizonAltitudeDeg, ExpectedHsDeg = ExpectedHsDeg,
                ConfiguredUtc = Utc, ObserverLatDeg = ObserverLatDeg, ObserverLonDeg = ObserverLonDeg
            };
        }
    }
}
