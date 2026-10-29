using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonRunner
{
    /// <summary>
    /// New Input System bindings defined in code (Xbox + keyboard/mouse), with
    /// press timestamps so gameplay can buffer inputs.
    /// </summary>
    public class PlayerInputReader : MonoBehaviour
    {
        InputActionMap map;
        InputAction move, jump, dash, grapple, taser, smoke, pause;
        string appliedOverrides;

        float suppressedUntil = float.NegativeInfinity;

        public Vector2 Move { get; private set; }
        public bool JumpHeld => jump.IsPressed() && !Suppressed;
        public bool GrappleHeld => grapple.IsPressed() && !Suppressed;
        /// <summary>True while the player is stunned: gameplay input reads as nothing pressed (pause still works).</summary>
        public bool Suppressed => Time.time < suppressedUntil;
        public float LastJumpPressTime { get; private set; } = float.NegativeInfinity;
        public float LastDashPressTime { get; private set; } = float.NegativeInfinity;
        public float LastGrapplePressTime { get; private set; } = float.NegativeInfinity;
        public bool GrappleAimedWithMouse { get; private set; }
        public bool TaserAimedWithMouse { get; private set; }
        public bool JumpPressedThisFrame { get; private set; }
        public bool TaserPressed { get; private set; }
        public bool SmokePressed { get; private set; }
        public bool PausePressed { get; private set; }

        void Awake()
        {
            map = CreateMap();
            move = map.FindAction("Move");
            jump = map.FindAction("Jump");
            dash = map.FindAction("Dash");
            grapple = map.FindAction("Grapple");
            taser = map.FindAction("Taser");
            smoke = map.FindAction("Smoke");
            pause = map.FindAction("Pause");
            ApplyBindingOverrides();
            GameSettings.Changed += ApplyBindingOverrides;
        }

        /// <summary>
        /// The player's bindings. Built fresh on each call so the settings menu can rebind its own
        /// copy (a map must be disabled to rebind) and save the overrides for every reader.
        /// </summary>
        public static InputActionMap CreateMap()
        {
            var map = new InputActionMap("Player");

            var move = map.AddAction("Move", InputActionType.Value);
            move.AddBinding("<Gamepad>/leftStick");
            move.AddBinding("<Gamepad>/dpad");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");

            AddButton(map, "Jump", "<Gamepad>/buttonSouth", "<Keyboard>/space", "<Keyboard>/z");
            AddButton(map, "Dash", "<Gamepad>/buttonWest", "<Keyboard>/leftShift", "<Keyboard>/x");
            AddButton(map, "Grapple", "<Gamepad>/rightShoulder", "<Keyboard>/e", "<Mouse>/rightButton");
            AddButton(map, "Taser", "<Gamepad>/buttonEast", "<Keyboard>/q", "<Mouse>/leftButton");
            AddButton(map, "Smoke", "<Gamepad>/leftShoulder", "<Keyboard>/r", "<Keyboard>/f");
            AddButton(map, "Pause", "<Gamepad>/start", "<Keyboard>/escape");
            return map;
        }

        static void AddButton(InputActionMap map, string name, params string[] paths)
        {
            var action = map.AddAction(name, InputActionType.Button);
            foreach (var path in paths) action.AddBinding(path);
        }

        void ApplyBindingOverrides()
        {
            if (appliedOverrides == GameSettings.BindingOverrides) return;
            appliedOverrides = GameSettings.BindingOverrides;
            map.RemoveAllBindingOverrides();
            LoadOverrides(map, GameSettings.BindingOverrides);
        }

        // Overrides are saved as "Action|bindingIndex|path" lines rather than the Input System's JSON:
        // that format matches bindings by id, and a map built in code gets fresh ids every time.

        public static string SaveOverrides(InputActionMap map)
        {
            var lines = new System.Text.StringBuilder();
            foreach (var action in map.actions)
                for (int i = 0; i < action.bindings.Count; i++)
                    if (action.bindings[i].hasOverrides)
                        lines.Append(action.name).Append('|').Append(i).Append('|').Append(action.bindings[i].overridePath).Append('\n');
            return lines.ToString();
        }

        public static void LoadOverrides(InputActionMap map, string saved)
        {
            if (string.IsNullOrEmpty(saved)) return;
            foreach (var line in saved.Split('\n'))
            {
                var parts = line.Split('|');
                if (parts.Length != 3 || !int.TryParse(parts[1], out int index)) continue;
                var action = map.FindAction(parts[0]);
                if (action != null && index >= 0 && index < action.bindings.Count) action.ApplyBindingOverride(index, parts[2]);
            }
        }

        void OnEnable() => map.Enable();
        void OnDisable() => map.Disable();

        void OnDestroy()
        {
            GameSettings.Changed -= ApplyBindingOverrides;
            map.Dispose();
        }

        void Update()
        {
            PausePressed = pause.WasPressedThisFrame();
            if (Suppressed)
            {
                Move = Vector2.zero;
                JumpPressedThisFrame = TaserPressed = SmokePressed = false;
                return;
            }

            var raw = move.ReadValue<Vector2>();
            Move = new Vector2(ApplyDeadzone(raw.x), ApplyDeadzone(raw.y));

            JumpPressedThisFrame = jump.WasPressedThisFrame();
            if (JumpPressedThisFrame) LastJumpPressTime = Time.time;
            if (dash.WasPressedThisFrame()) LastDashPressTime = Time.time;
            if (grapple.WasPressedThisFrame())
            {
                LastGrapplePressTime = Time.time;
                GrappleAimedWithMouse = grapple.activeControl?.device is Mouse;
            }

            TaserPressed = taser.WasPressedThisFrame();
            if (TaserPressed) TaserAimedWithMouse = taser.activeControl?.device is Mouse;
            SmokePressed = smoke.WasPressedThisFrame();
        }

        /// <summary>The keyboard key currently bound to an action (after rebinding), for HUD prompts.</summary>
        public string KeyLabel(string actionName)
        {
            var action = map.FindAction(actionName);
            for (int i = 0; i < action.bindings.Count; i++)
                if (!action.bindings[i].isPartOfComposite && action.bindings[i].path.StartsWith("<Keyboard>"))
                    return action.GetBindingDisplayString(i).ToUpperInvariant();
            return "?";
        }

        public void Suppress(float duration) => suppressedUntil = Mathf.Max(suppressedUntil, Time.time + duration);
        public void ClearSuppress() => suppressedUntil = float.NegativeInfinity;

        static float ApplyDeadzone(float v) => Mathf.Abs(v) < GameSettings.StickDeadzone ? 0f : v;

        public void ConsumeJump() => LastJumpPressTime = float.NegativeInfinity;
        public void ConsumeDash() => LastDashPressTime = float.NegativeInfinity;
        public void ConsumeGrapple() => LastGrapplePressTime = float.NegativeInfinity;

        public static Vector2 MouseWorldPosition(Camera cam)
        {
            if (Mouse.current == null || cam == null) return Vector2.zero;
            return cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        }
    }
}
