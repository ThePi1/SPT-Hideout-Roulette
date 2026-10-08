using EFT;
using EFT.UI;
using UnityEngine;

namespace HideoutRoulette.Client.Wheel
{
    /// <summary>
    ///     Top down view of the table where the mouse places chips directly on the 3D betting spots
    /// </summary>
    public class TablePlayMode : MonoBehaviour
    {
        private const float HighlightLift = 0.004f;
        private const float HudWidth = 560f;
        private const float HudHeight = 195f;
        private const float HudMargin = 20f;

        // Time the camera takes to travel between the players eyes and the play view
        private const float TransitionSeconds = 0.5f;
        private const float PlayFieldOfView = 60f;

        public RouletteTable Table;
        public Camera Camera;
        public Light Lamp;
        public Transform Highlight;

        // Point on the table the camera looks at and how far back it sits, in the tables local space
        public Vector3 CameraPivot;
        public float CameraDistance;

        // Height of the playing surface in the tables local space
        public float FeltHeight;

        // Disc the wheel is clicked on: its radius, and the height of its rim in the tables local space
        public float WheelRadius;
        public float WheelRimHeight;

        private HideoutPlayerOwner _owner;

        // The games dot in the middle of the screen, hidden while the cursor is in use
        private UIPointer _pointer;
        private int _chipIndex;
        private GUIStyle _hudStyle;

        // 0 at the players own view, 1 in the play view. Betting only works once it has fully arrived.
        private float _transition;
        private bool _leaving;

        // The players view when they sat down, where the camera starts from and returns to
        private Vector3 _eyePosition;
        private Quaternion _eyeRotation;
        private float _eyeFieldOfView;

        /// <summary>
        ///     Table currently being played, null when the player is walking around
        /// </summary>
        public static TablePlayMode Current { get; private set; }

        public void Enter(HideoutPlayerOwner owner)
        {
            if (Current != null)
            {
                return;
            }

            Current = this;
            _owner = owner;

            GamePlayerOwner.SetIgnoreInput(true);
            owner.ClearInteractionState();

            // Start exactly where the player is looking from, or already in place if their camera cannot be found
            _leaving = false;
            _transition = 1f;
            if (CameraClass.Exist && CameraClass.Instance.Camera != null)
            {
                var eyes = CameraClass.Instance.Camera;
                _eyePosition = eyes.transform.position;
                _eyeRotation = eyes.transform.rotation;
                _eyeFieldOfView = eyes.fieldOfView;
                _transition = 0f;
            }

            Camera.enabled = true;
            Lamp.enabled = true;
            ApplyCamera();
            ShowCursor(true);

            _pointer = FindObjectOfType<UIPointer>();
            SetPointerVisible(false);
        }

        /// <summary>
        ///     Leave the play view. The camera travels back to the players eyes first unless told to cut straight away.
        /// </summary>
        public void Exit(bool immediately = false)
        {
            if (Current != this)
            {
                return;
            }

            if (!immediately && _transition > 0f)
            {
                // Nothing more to do if it is already on its way back
                if (!_leaving)
                {
                    _leaving = true;
                    Highlight.gameObject.SetActive(false);
                    ShowCursor(false);
                }

                return;
            }

            Current = null;
            _leaving = false;
            Camera.enabled = false;
            Lamp.enabled = false;
            Highlight.gameObject.SetActive(false);
            ShowCursor(false);
            SetPointerVisible(true);

            // Unity null check, the owner is gone if the hideout unloaded. Outside first person the game ignores input itself.
            if (_owner != null)
            {
                GamePlayerOwner.SetIgnoreInput(!_owner.FirstPersonMode);

                // The player is still looking at the table, bring the prompt back
                _owner.InteractionsChangedHandler();
            }
        }

        private void OnDisable()
        {
            Exit(true);
        }

        private void Update()
        {
            if (Current != this)
            {
                return;
            }

            // The hideout view the camera would return to is gone
            if (_owner == null || !_owner.FirstPersonMode)
            {
                Exit(true);

                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Exit();
            }

            _transition = Mathf.Clamp01(_transition + (_leaving ? -1f : 1f) * Time.deltaTime / TransitionSeconds);
            ApplyCamera();

            if (_leaving)
            {
                if (_transition <= 0f)
                {
                    Exit(true);
                }

                return;
            }

            // The game hides and locks the cursor again whenever its own screens change
            if (!Cursor.visible || Cursor.lockState == CursorLockMode.Locked)
            {
                ShowCursor(true);
            }

            if (_transition < 1f)
            {
                return;
            }

            UpdateChipSelection();

            var spot = PickSpot(out var onWheel);
            UpdateHighlight(spot);

            if (Table.Spinning)
            {
                return;
            }

            var chip = Table.ChipValues[_chipIndex];
            if (Input.GetMouseButtonDown(0))
            {
                if (spot != null)
                {
                    Table.AddBet(spot, chip);
                }
                else if (onWheel)
                {
                    Table.Spin();
                }
            }
            else if (Input.GetMouseButtonDown(1) && spot != null)
            {
                Table.RemoveFromBet(spot, chip);
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                Table.Spin();
            }
            else if (Input.GetKeyDown(KeyCode.C))
            {
                Table.ClearAllBets();
            }
            else if (Input.GetKeyDown(KeyCode.R) && Table.TotalBet == 0 && Table.HasLastBets)
            {
                Table.RepeatLastBets();
            }
        }

        /// <summary>
        ///     Place the camera for the current point in the transition. The play view is swung back from overhead towards
        ///     the players side by the configured angle, always aimed at the pivot.
        /// </summary>
        private void ApplyCamera()
        {
            var angle = Plugin.CameraAngle.Value;
            var radians = angle * Mathf.Deg2Rad;

            // The player stands on the -Z side of the table
            var offset = new Vector3(0f, Mathf.Cos(radians), -Mathf.Sin(radians)) * CameraDistance;
            var playPosition = transform.TransformPoint(CameraPivot + offset);
            var playRotation = transform.rotation * Quaternion.Euler(90f - angle, 0f, 0f);

            // Eases out of one view and into the other
            var blend = Mathf.SmoothStep(0f, 1f, _transition);
            Camera.transform.SetPositionAndRotation(
                Vector3.Lerp(_eyePosition, playPosition, blend),
                Quaternion.Slerp(_eyeRotation, playRotation, blend)
            );
            Camera.fieldOfView = Mathf.Lerp(_eyeFieldOfView, PlayFieldOfView, blend);
        }

        private void UpdateChipSelection()
        {
            var chipCount = Table.ChipValues.Length;
            var scroll = Input.mouseScrollDelta.y;
            if (scroll > 0f)
            {
                _chipIndex = Mathf.Min(_chipIndex + 1, chipCount - 1);
            }
            else if (scroll < 0f)
            {
                _chipIndex = Mathf.Max(_chipIndex - 1, 0);
            }

            for (var i = 0; i < chipCount; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    _chipIndex = i;
                }
            }
        }

        private BetSpot PickSpot(out bool onWheel)
        {
            onWheel = false;

            var ray = Camera.ScreenPointToRay(Input.mousePosition);

            // Worked out from where the cursor meets the table rather than with a physics ray. Spot colliders stand above
            // the felt, so from an angled camera a ray would hit the side of a neighbouring spot before reaching the printed
            // cell, and other interactable volumes in the hideout can sit between the camera and the table.
            var origin = transform.InverseTransformPoint(ray.origin);
            var direction = transform.InverseTransformDirection(ray.direction);
            if (direction.y >= 0f)
            {
                return null;
            }

            // The wheel is a disc at the height of its rim
            var onRim = origin + direction * ((WheelRimHeight - origin.y) / direction.y);
            var fromAxis = onRim - Table.WheelCentre;
            if (new Vector2(fromAxis.x, fromAxis.z).magnitude <= WheelRadius)
            {
                onWheel = true;

                return null;
            }

            var onFelt = origin + direction * ((FeltHeight - origin.y) / direction.y);
            foreach (var spot in Table.Spots)
            {
                var offset = onFelt - spot.transform.localPosition;
                if (Mathf.Abs(offset.x) <= spot.Size.x / 2f && Mathf.Abs(offset.z) <= spot.Size.y / 2f)
                {
                    return spot;
                }
            }

            return null;
        }

        private void UpdateHighlight(BetSpot spot)
        {
            Highlight.gameObject.SetActive(spot != null && !Table.Spinning);
            if (spot == null)
            {
                return;
            }

            Highlight.localPosition = spot.transform.localPosition + Vector3.up * HighlightLift;
            Highlight.localScale = new Vector3(spot.Size.x, spot.Size.y, 1f);
        }

        private void OnGUI()
        {
            // Only once the camera has settled on the table
            if (Current != this || _leaving || _transition < 1f)
            {
                return;
            }

            if (_hudStyle == null)
            {
                _hudStyle = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.UpperLeft, padding = new RectOffset(12, 12, 10, 10) };
            }

            var chips = string.Empty;
            for (var i = 0; i < Table.ChipValues.Length; i++)
            {
                var value = Table.ChipValues[i].ToString("N0");
                chips += i == _chipIndex ? $"  [{value}]" : $"   {value} ";
            }

            var status = Table.Spinning ? "No more bets, the wheel is spinning" : "Left click a spot to add a chip, right click to remove one";
            var text =
                $"ROULETTE\n"
                + $"Chip (scroll or 1-3):{chips}\n"
                + $"Total bet: {Table.TotalBet:N0} roubles\n"
                + $"{status}\n"
                + "Click the wheel or press Space to spin\n"
                + "C clear bets    R repeat last bets    Esc leave\n"
                + $"Last spin: {Table.LastResult ?? "none yet"}";

            // Pinned to the top right corner
            GUI.Box(new Rect(Screen.width - HudWidth - HudMargin, HudMargin, HudWidth, HudHeight), text, _hudStyle);
        }

        private void SetPointerVisible(bool visible)
        {
            // Unity null check, the games UI may have been rebuilt since the pointer was found
            if (_pointer != null)
            {
                _pointer.gameObject.SetActive(visible);
            }
        }

        private static void ShowCursor(bool visible)
        {
            Cursor.visible = visible;
            GClass3746.SetCursorLockMode(visible, Screen.fullScreenMode);
            GClass3746.SetCursor(visible ? ECursorType.Idle : ECursorType.Invisible);
        }
    }
}
