using Touge.Core;
using Touge.Vehicle;
using Touge.Vehicle.Physics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Touge.UI
{
    /// <summary>
    /// Tuning telemetry overlay, toggled with F1.
    ///
    /// Uses IMGUI on purpose. It needs no prefabs, no canvas, no fonts and no asset references, so it
    /// cannot drift out of sync with the scene and costs nothing to keep working as the project grows.
    /// It is a development tool, not the game's HUD - the real one will be UI Toolkit later.
    ///
    /// The per-wheel table is the important part. When the car does something you did not expect, the
    /// answer is almost always visible in the relationship between load, slip and grip usage on the
    /// four corners.
    /// </summary>
    [DisallowMultipleComponent]
    public class DebugHud : MonoBehaviour
    {
        [Tooltip("Car to report on. If empty, the first CarController in the scene is used.")]
        [SerializeField] private CarController car;

        [Tooltip("TougeControls.inputactions. Reads ToggleHud (F1) from the Debug map. Optional.")]
        [SerializeField] private InputActionAsset actionAsset;

        [Tooltip("Whether the overlay starts visible.")]
        [SerializeField] private bool visible = true;

        [Tooltip("Scales the whole overlay, for high-DPI displays.")]
        [Range(0.5f, 3f)]
        [SerializeField] private float uiScale = 1.3f;

        private InputAction _toggleAction;
        private GUIStyle _headerStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _barBackground;
        private GUIStyle _barFill;
        private Texture2D _backgroundTexture;
        private Texture2D _fillTexture;

        private void Awake()
        {
            if (car == null) car = FindFirstObjectByType<CarController>();

            InputActionMap debugMap = actionAsset != null
                ? actionAsset.FindActionMap("Debug", throwIfNotFound: false)
                : null;

            if (debugMap != null)
            {
                debugMap.Enable();
                _toggleAction = debugMap.FindAction("ToggleHud", throwIfNotFound: false);
            }
        }

        private void Update()
        {
            if (_toggleAction != null && _toggleAction.WasPressedThisFrame()) visible = !visible;
        }

        private void OnDestroy()
        {
            if (_backgroundTexture != null) Destroy(_backgroundTexture);
            if (_fillTexture != null) Destroy(_fillTexture);
        }

        private void OnGUI()
        {
            if (!visible || car == null) return;

            VehicleTelemetry t = car.Telemetry;
            if (t == null || t.Wheels == null) return;

            EnsureStyles();

            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * uiScale);

            const float panelWidth = 330f;
            GUILayout.BeginArea(new Rect(10f, 10f, panelWidth, 620f), GUI.skin.box);

            DrawVehicleSection(t);
            DrawDriftSection(t);
            DrawInputSection(t);
            DrawWheelTable(t);

            GUILayout.EndArea();
            GUI.matrix = previousMatrix;
        }

        private void DrawVehicleSection(VehicleTelemetry t)
        {
            GUILayout.Label("VEHICLE", _headerStyle);

            GUILayout.Label($"Speed      {t.SpeedKph,7:F1} km/h", _labelStyle);
            GUILayout.Label($"RPM        {t.EngineRpm,7:F0}{(t.RevLimiterActive ? "  [LIMIT]" : "")}", _labelStyle);
            GUILayout.Label($"Gear       {GearLabel(t.Gear),7}   {(t.TransmissionMode == TransmissionMode.Manual ? "MT" : "AT")}", _labelStyle);
            GUILayout.Label($"Clutch T   {t.ClutchTorqueNm,7:F0} Nm{(t.ClutchSlipping ? "  [SLIP]" : "")}", _labelStyle);
            GUILayout.Label($"Accel      {t.LongitudinalG,6:F2} g lon / {t.LateralG,5:F2} g lat", _labelStyle);
            GUILayout.Label($"Yaw rate   {t.YawRateDegPerSec,7:F1} deg/s", _labelStyle);
            GUILayout.Label($"Load sum   {t.TotalLoadN,7:F0} N   ({t.GroundedWheelCount}/4 down)", _labelStyle);

            GUILayout.Space(4f);
        }

        private void DrawDriftSection(VehicleTelemetry t)
        {
            GUILayout.Label("DRIFT", _headerStyle);

            string state = t.IsDrifting ? "ACTIVE" : "-";
            GUILayout.Label($"Angle      {t.DriftAngleDeg,7:F1} deg   {state}", _labelStyle);
            GUILayout.Label($"Duration   {t.DriftDuration,7:F2} s", _labelStyle);
            GUILayout.Label($"Score      {t.CurrentDriftScore,7:F0}  (total {t.TotalDriftScore:F0})", _labelStyle);

            GUILayout.Space(4f);
        }

        private void DrawInputSection(VehicleTelemetry t)
        {
            GUILayout.Label("INPUT", _headerStyle);

            DrawBar("Throttle", t.Throttle, Color.green);
            DrawBar("Brake", t.Brake, Color.red);
            DrawBar("Handbrake", t.Handbrake, new Color(1f, 0.55f, 0f));
            DrawBar("Clutch", t.Clutch, Color.cyan);
            DrawBar("Steer", Mathf.Abs(t.Steer), Color.white);

            string assist = Mathf.Abs(t.CounterSteerAssistDeg) > 0.05f
                ? $"  (assist {t.CounterSteerAssistDeg:+0.0;-0.0})"
                : "";
            GUILayout.Label($"Steer angle {t.SteerAngleDeg,6:F1} deg{assist}", _labelStyle);

            GUILayout.Space(4f);
        }

        private void DrawWheelTable(VehicleTelemetry t)
        {
            GUILayout.Label("WHEELS", _headerStyle);
            GUILayout.Label("      load N   slipR  slipA   grip", _labelStyle);

            foreach (Wheel wheel in t.Wheels)
            {
                if (wheel == null) continue;

                string air = wheel.IsGrounded ? " " : "*";
                GUILayout.Label(
                    $"{wheel.Label}{air} {wheel.Load,7:F0}  {wheel.SlipRatio,6:F2} {wheel.SlipAngleDeg,6:F1}  {wheel.GripUsage * 100f,4:F0}%",
                    _labelStyle);
            }

            GUILayout.Space(2f);

            // Grip usage bars make it obvious at a glance which corner ran out of grip first.
            foreach (Wheel wheel in t.Wheels)
            {
                if (wheel == null) continue;
                Color colour = wheel.GripUsage > 0.98f ? Color.red
                    : wheel.GripUsage > 0.85f ? new Color(1f, 0.7f, 0f)
                    : Color.green;
                DrawBar(wheel.Label, Mathf.Clamp01(wheel.GripUsage), colour);
            }

            GUILayout.Label("* = airborne", _labelStyle);
        }

        private void DrawBar(string label, float value01, Color colour)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _labelStyle, GUILayout.Width(78f));

            Rect rect = GUILayoutUtility.GetRect(180f, 12f);
            GUI.DrawTexture(rect, _backgroundTexture);

            Rect fill = new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(value01), rect.height);
            Color previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(fill, _fillTexture);
            GUI.color = previous;

            GUILayout.EndHorizontal();
        }

        private static string GearLabel(int gear) => gear switch
        {
            -1 => "R",
            0 => "N",
            _ => gear.ToString()
        };

        private void EnsureStyles()
        {
            if (_headerStyle != null) return;

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.55f, 0.85f, 1f) }
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Normal,
                richText = false,
                normal = { textColor = Color.white }
            };

            // Monospacing matters here: the wheel table is unreadable if the columns move as values
            // change. Unity's built-in skin has no monospaced font, so the format strings above use
            // fixed field widths instead, which gets most of the way there.
            _backgroundTexture = MakeTexture(new Color(0f, 0f, 0f, 0.55f));
            _fillTexture = MakeTexture(Color.white);
        }

        private static Texture2D MakeTexture(Color colour)
        {
            Texture2D texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, colour);
            texture.Apply();
            return texture;
        }
    }
}
