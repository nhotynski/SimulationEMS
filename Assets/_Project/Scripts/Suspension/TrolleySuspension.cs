using System;
using System.Collections.Generic;
using IndustrialConveyor.Simulation.Core;
using IndustrialConveyor.Simulation.Track;
using IndustrialConveyor.Simulation.TrolleyTypes;
using UnityEngine;

namespace IndustrialConveyor.Simulation.Suspension
{
    /// <summary>Positions both wheel assemblies from independent samples of the mathematical track.</summary>
    [DisallowMultipleComponent]
    public sealed class TrolleySuspension : MonoBehaviour
    {
        [SerializeField] private TrackConfiguration trackConfiguration;
        [SerializeField] private TrolleyTypeDefinition trolleyType;
        [SerializeField] private Transform trackFrame;
        [SerializeField] private Transform bodyAnchor;
        [SerializeField] private Transform frontWheelAssemblyAnchor;
        [SerializeField] private Transform rearWheelAssemblyAnchor;
        [SerializeField, Min(0f)] private float trackElevationMeters = 8f;
        [SerializeField] private Vector3 bodyRotationOffsetDegrees;
        [SerializeField, Min(0f)] private float initialCenterPositionMeters;

        private TrackNetwork network;
        private GameObject bodyInstance;
        private GameObject frontWheelInstance;
        private GameObject rearWheelInstance;
        private MaterialPropertyBlock bodyMaterialProperties;
        private static Material sharedBodyMaterial;
        private static Material sharedWheelMaterial;

        /// <summary>Gets the configured trolley type.</summary>
        public TrolleyTypeDefinition TrolleyType => trolleyType;

        /// <summary>Gets the last world-space front wheel position.</summary>
        public Vector3 FrontWheelPosition { get; private set; }

        /// <summary>Gets the last world-space rear wheel position.</summary>
        public Vector3 RearWheelPosition { get; private set; }

        /// <summary>Gets the last world-space body position.</summary>
        public Vector3 BodyPosition { get; private set; }

        /// <summary>Gets the last world-space front wheel rotation.</summary>
        public Quaternion FrontWheelRotation { get; private set; }

        /// <summary>Gets the last world-space rear wheel rotation.</summary>
        public Quaternion RearWheelRotation { get; private set; }

        /// <summary>Gets the last world-space body rotation derived from both wheel positions.</summary>
        public Quaternion BodyRotation { get; private set; }

        /// <summary>Gets the geometry of the main track edge (the whole loop on a simple loop).</summary>
        public TrackPath TrackPath => network != null ? network.Edges[0].Path : null;

        /// <summary>Gets the track network the suspension samples.</summary>
        public TrackNetwork Network => network;

        /// <summary>Gets the configured starting center position in meters along the track.</summary>
        public float InitialCenterPositionMeters => initialCenterPositionMeters;

        /// <summary>Gets the world-height offset from the mathematical path to the overhead rail center.</summary>
        public float TrackElevationMeters => trackElevationMeters;

        private void Awake()
        {
            Initialize();
        }

        /// <summary>Validates references, resolves the track network, and creates the configured visuals.</summary>
        public void Initialize()
        {
            if (trackConfiguration == null)
                throw new InvalidOperationException($"{nameof(TrolleySuspension)} on '{name}' requires a track configuration.");
            if (trolleyType == null)
                throw new InvalidOperationException($"{nameof(TrolleySuspension)} on '{name}' requires a trolley type.");
            if (!trolleyType.TryValidate(out var typeError))
                throw new InvalidOperationException(typeError);
            if (bodyAnchor == null || frontWheelAssemblyAnchor == null || rearWheelAssemblyAnchor == null)
                throw new InvalidOperationException($"{nameof(TrolleySuspension)} on '{name}' requires body, front-wheel, and rear-wheel anchors.");

            network = TrackSource.ResolveNetwork(trackConfiguration);
            CreateVisualInstances();
            UpdateSuspension(initialCenterPositionMeters);
        }

        /// <summary>Changes this suspension's runtime starting distance and immediately updates its visuals.</summary>
        public void SetInitialCenterPositionMeters(float distanceMeters)
        {
            if (float.IsNaN(distanceMeters) || float.IsInfinity(distanceMeters))
                throw new ArgumentOutOfRangeException(nameof(distanceMeters), "Trolley center position must be finite.");

            initialCenterPositionMeters = distanceMeters;
            if (network != null)
                UpdateSuspension(initialCenterPositionMeters);
        }

        /// <summary>Changes this trolley's type and rebuilds its visuals.</summary>
        public void SetTrolleyType(TrolleyTypeDefinition newType)
        {
            if (newType == null)
                throw new ArgumentNullException(nameof(newType));
            if (!newType.TryValidate(out var typeError))
                throw new InvalidOperationException(typeError);
            if (trolleyType == newType)
                return;

            ClearVisualChildren(bodyAnchor);
            ClearVisualChildren(frontWheelAssemblyAnchor);
            ClearVisualChildren(rearWheelAssemblyAnchor);
            bodyInstance = null;
            frontWheelInstance = null;
            rearWheelInstance = null;
            trolleyType = newType;

            if (network != null)
            {
                CreateVisualInstances();
                UpdateSuspension(initialCenterPositionMeters);
            }
        }

        /// <summary>
        /// Places the trolley at a distance along the main route (the whole loop on a simple loop), sampling both wheels
        /// independently around the center and aligning the body to them.
        /// </summary>
        public void UpdateSuspension(float trolleyCenterPosition)
        {
            if (network == null)
                Initialize();
            if (float.IsNaN(trolleyCenterPosition) || float.IsInfinity(trolleyCenterPosition))
                throw new ArgumentOutOfRangeException(nameof(trolleyCenterPosition), "Trolley center position must be finite.");

            var location = network.ResolveMainRoute(trolleyCenterPosition);
            UpdateSuspension(location.Edge, location.Meters, null);
        }

        /// <summary>
        /// Places the trolley at a point of a track edge. The front wheel follows the active switches ahead; the rear wheel
        /// follows the edges the trolley has actually travelled (<paramref name="trail"/>, most recent first).
        /// </summary>
        public void UpdateSuspension(TrackEdge edge, float edgeMeters, IReadOnlyList<TrackEdge> trail)
        {
            if (network == null)
                Initialize();
            if (edge == null)
                throw new ArgumentNullException(nameof(edge));
            if (float.IsNaN(edgeMeters) || float.IsInfinity(edgeMeters))
                throw new ArgumentOutOfRangeException(nameof(edgeMeters), "Trolley center position must be finite.");

            var halfWheelbase = trolleyType.WheelbaseMeters * 0.5f;
            var frontSample = network.SampleOffset(edge, edgeMeters, halfWheelbase, trail);
            var rearSample = network.SampleOffset(edge, edgeMeters, -halfWheelbase, trail);
            ApplySamples(frontSample, rearSample);
        }

        private void ApplySamples(TrackSample frontSample, TrackSample rearSample)
        {
            var frame = trackFrame != null ? trackFrame : transform;

            FrontWheelPosition = ToWorldPosition(frame, frontSample, trolleyType.FrontWheelAssemblyOffsetMeters);
            RearWheelPosition = ToWorldPosition(frame, rearSample, trolleyType.RearWheelAssemblyOffsetMeters);
            FrontWheelRotation = ToWorldRotation(frame, frontSample, trolleyType.FrontWheelRotationOffsetDegrees);
            RearWheelRotation = ToWorldRotation(frame, rearSample, trolleyType.RearWheelRotationOffsetDegrees);

            var frontUp = frame.TransformDirection(frontSample.Normal).normalized;
            var rearUp = frame.TransformDirection(rearSample.Normal).normalized;
            var bodyUp = (frontUp + rearUp).normalized;
            if (bodyUp.sqrMagnitude < 0.0001f)
                bodyUp = frontUp;

            var bodyForward = Vector3.ProjectOnPlane(FrontWheelPosition - RearWheelPosition, bodyUp).normalized;
            if (bodyForward.sqrMagnitude < 0.0001f)
                bodyForward = frame.TransformDirection(frontSample.Tangent).normalized;

            BodyPosition = (FrontWheelPosition + RearWheelPosition) * 0.5f - bodyUp * trolleyType.SuspensionHeightMeters;
            BodyRotation = Quaternion.LookRotation(bodyForward, bodyUp) * Quaternion.Euler(bodyRotationOffsetDegrees);
            ApplyVisualTransforms();
        }

        private Vector3 ToWorldPosition(Transform frame, TrackSample sample, Vector3 localOffset)
        {
            return frame.TransformPoint(sample.Position + sample.Normal * trackElevationMeters + sample.Rotation * localOffset);
        }

        private static Quaternion ToWorldRotation(Transform frame, TrackSample sample, Vector3 rotationOffsetDegrees)
        {
            return frame.rotation * sample.Rotation * Quaternion.Euler(rotationOffsetDegrees);
        }

        private void CreateVisualInstances()
        {
            if (bodyInstance == null)
                bodyInstance = FindExistingVisual(bodyAnchor, $"{trolleyType.TypeId}_Body") ??
                               Instantiate(trolleyType.BodyPrefab, bodyAnchor, false);
            if (frontWheelInstance == null)
                frontWheelInstance = FindExistingVisual(frontWheelAssemblyAnchor, "Front Wheel Assembly") ??
                                     Instantiate(trolleyType.WheelAssemblyPrefab, frontWheelAssemblyAnchor, false);
            if (rearWheelInstance == null)
                rearWheelInstance = FindExistingVisual(rearWheelAssemblyAnchor, "Rear Wheel Assembly") ??
                                    Instantiate(trolleyType.WheelAssemblyPrefab, rearWheelAssemblyAnchor, false);

            bodyInstance.name = $"{trolleyType.TypeId}_Body";
            frontWheelInstance.name = "Front Wheel Assembly";
            rearWheelInstance.name = "Rear Wheel Assembly";
            bodyInstance.transform.localScale = new Vector3(
                trolleyType.BodyWidthMeters, trolleyType.BodyHeightMeters, trolleyType.BodyLengthMeters);
            ApplyVisualMaterial(bodyInstance, ref sharedBodyMaterial, trolleyType.BodyColor);
            ApplyVisualMaterial(frontWheelInstance, ref sharedWheelMaterial, new Color(0.13f, 0.15f, 0.17f));
            ApplyVisualMaterial(rearWheelInstance, ref sharedWheelMaterial, new Color(0.13f, 0.15f, 0.17f));
        }

        private static GameObject FindExistingVisual(Transform anchor, string visualName)
        {
            var existing = anchor.Find(visualName);
            return existing != null ? existing.gameObject : null;
        }

        private static void ClearVisualChildren(Transform anchor)
        {
            if (anchor == null)
                return;

            for (var index = anchor.childCount - 1; index >= 0; index--)
            {
                var child = anchor.GetChild(index).gameObject;
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }
        }

        private void ApplyVisualTransforms()
        {
            bodyAnchor.SetPositionAndRotation(BodyPosition, BodyRotation);
            frontWheelAssemblyAnchor.SetPositionAndRotation(FrontWheelPosition, FrontWheelRotation);
            rearWheelAssemblyAnchor.SetPositionAndRotation(RearWheelPosition, RearWheelRotation);
        }

        private void ApplyVisualMaterial(GameObject visual, ref Material sharedMaterial, Color color)
        {
            if (sharedMaterial == null)
            {
                var shader = ConveyorShaders.Lit;
                if (shader == null)
                    return;
                sharedMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            if (bodyMaterialProperties == null)
                bodyMaterialProperties = new MaterialPropertyBlock();
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                renderer.sharedMaterial = sharedMaterial;
                renderer.GetPropertyBlock(bodyMaterialProperties);
                if (sharedMaterial.HasProperty("_BaseColor"))
                    bodyMaterialProperties.SetColor("_BaseColor", color);
                else
                    bodyMaterialProperties.SetColor("_Color", color);
                renderer.SetPropertyBlock(bodyMaterialProperties);
            }
        }
    }
}