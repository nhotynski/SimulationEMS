using System;
using System.Collections.Generic;
using UnityEngine;

namespace IndustrialConveyor.Simulation.TrolleyTypes
{
    /// <summary>A user-defined key/value extension for a trolley type.</summary>
    [Serializable]
    public sealed class TrolleyCustomParameter
    {
        [SerializeField] private string key;
        [SerializeField] private string value;

        /// <summary>Gets the custom parameter key.</summary>
        public string Key => key;

        /// <summary>Gets the custom parameter value.</summary>
        public string Value => value;
    }

    /// <summary>Configures a reusable trolley type without coupling type data to movement code.</summary>
    [CreateAssetMenu(menuName = "Industrial Conveyor/Trolley Type", fileName = "TrolleyType")]
    public sealed class TrolleyTypeDefinition : ScriptableObject
    {
        [SerializeField] private string typeId;
        [SerializeField] private string displayName;
        [SerializeField, Min(0.01f)] private float bodyLengthMeters = 3f;
        [SerializeField, Min(0.01f)] private float wheelbaseMeters = 2.4f;
        [SerializeField] private GameObject bodyPrefab;
        [SerializeField] private GameObject wheelAssemblyPrefab;
        [SerializeField, Min(0f)] private float suspensionHeightMeters = 1.2f;
        [SerializeField] private Vector3 frontWheelAssemblyOffsetMeters;
        [SerializeField] private Vector3 rearWheelAssemblyOffsetMeters;
        [SerializeField] private Vector3 frontWheelRotationOffsetDegrees;
        [SerializeField] private Vector3 rearWheelRotationOffsetDegrees;
        [SerializeField] private Vector3 barcodeReaderMountingPositionMeters = new Vector3(0f, -0.5f, 0f);
        [SerializeField, Min(0f)] private float maximumSpeedMetersPerSecond = 1.5f;
        [SerializeField, Min(0.001f)] private float accelerationMetersPerSecondSquared = 0.3f;
        [SerializeField, Min(0.001f)] private float decelerationMetersPerSecondSquared = 0.5f;
        [SerializeField, Min(0f)] private float weightKilograms = 250f;
        [SerializeField, Min(0.01f)] private float bodyWidthMeters = 0.8f;
        [SerializeField, Min(0.01f)] private float bodyHeightMeters = 0.6f;
        [SerializeField] private Color bodyColor = new Color(0.72f, 0.75f, 0.77f);
        [SerializeField] private List<TrolleyCustomParameter> customParameters = new List<TrolleyCustomParameter>();

        /// <summary>Gets the stable unique type identifier.</summary>
        public string TypeId => typeId;

        /// <summary>Gets the operator-facing name.</summary>
        public string DisplayName => displayName;

        /// <summary>Gets the trolley body length in meters.</summary>
        public float BodyLengthMeters => bodyLengthMeters;

        /// <summary>Gets the distance between front and rear wheel assemblies in meters.</summary>
        public float WheelbaseMeters => wheelbaseMeters;

        /// <summary>Gets the unit-scale body visual prefab.</summary>
        public GameObject BodyPrefab => bodyPrefab;

        /// <summary>Gets the wheel assembly visual prefab.</summary>
        public GameObject WheelAssemblyPrefab => wheelAssemblyPrefab;

        /// <summary>Gets the vertical distance from rail center to body center in meters.</summary>
        public float SuspensionHeightMeters => suspensionHeightMeters;

        /// <summary>Gets the front wheel offset in the sampled track frame.</summary>
        public Vector3 FrontWheelAssemblyOffsetMeters => frontWheelAssemblyOffsetMeters;

        /// <summary>Gets the rear wheel offset in the sampled track frame.</summary>
        public Vector3 RearWheelAssemblyOffsetMeters => rearWheelAssemblyOffsetMeters;

        /// <summary>Gets the front wheel rotation offset in degrees.</summary>
        public Vector3 FrontWheelRotationOffsetDegrees => frontWheelRotationOffsetDegrees;

        /// <summary>Gets the rear wheel rotation offset in degrees.</summary>
        public Vector3 RearWheelRotationOffsetDegrees => rearWheelRotationOffsetDegrees;

        /// <summary>Gets the barcode reader mount position in body-local meters.</summary>
        public Vector3 BarcodeReaderMountingPositionMeters => barcodeReaderMountingPositionMeters;

        /// <summary>Gets the future drive speed limit in meters per second.</summary>
        public float MaximumSpeedMetersPerSecond => maximumSpeedMetersPerSecond;

        /// <summary>Gets the future drive acceleration in meters per second squared.</summary>
        public float AccelerationMetersPerSecondSquared => accelerationMetersPerSecondSquared;

        /// <summary>Gets the future drive deceleration in meters per second squared.</summary>
        public float DecelerationMetersPerSecondSquared => decelerationMetersPerSecondSquared;

        /// <summary>Gets the optional trolley weight in kilograms.</summary>
        public float WeightKilograms => weightKilograms;

        /// <summary>Gets the body width in meters.</summary>
        public float BodyWidthMeters => bodyWidthMeters;

        /// <summary>Gets the body height in meters.</summary>
        public float BodyHeightMeters => bodyHeightMeters;

        /// <summary>Gets the body tint used by the shared visual prefab.</summary>
        public Color BodyColor => bodyColor;

        /// <summary>Gets optional project-specific values for future extensions.</summary>
        public IReadOnlyList<TrolleyCustomParameter> CustomParameters => customParameters;

        /// <summary>Checks that dimensions, identifiers, and required visual prefabs are configured.</summary>
        public bool TryValidate(out string error)
        {
            if (string.IsNullOrWhiteSpace(typeId))
            {
                error = "Trolley type ID must not be empty.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(displayName))
            {
                error = $"Trolley type '{typeId}' must have a display name.";
                return false;
            }
            if (!IsPositiveFinite(bodyLengthMeters) || !IsPositiveFinite(wheelbaseMeters))
            {
                error = $"Trolley type '{typeId}' must have positive finite body length and wheelbase values.";
                return false;
            }
            if (!IsPositiveFinite(bodyWidthMeters) || !IsPositiveFinite(bodyHeightMeters) ||
                float.IsNaN(suspensionHeightMeters) || float.IsInfinity(suspensionHeightMeters) || suspensionHeightMeters < 0f)
            {
                error = $"Trolley type '{typeId}' has invalid body or suspension dimensions.";
                return false;
            }
            if (bodyPrefab == null || wheelAssemblyPrefab == null)
            {
                error = $"Trolley type '{typeId}' requires body and wheel assembly prefabs.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}