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
            Camera.enabled = true;
            Lamp.enabled = true;
            ApplyCameraAngle();
            ShowCursor(true);

            _pointer = FindObjectOfType<UIPointer>();
            SetPointerVisible(false);
        }

        public void Exit()
        {
            if (Current != this)
            {
                return;
            }

            Current = null;
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
            Exit();
        }

        private void Update()
        {
            if (Current != this)
            {
                return;
            }

            if (_owner == null || !_owner.FirstPersonMode || Input.GetKeyDown(KeyCode.Escape))
            {
                Exit();

                return;
            }

            // The game hides and locks the cursor again whenever its own screens change
            if (!Cursor.visible || Cursor.lockState == CursorLockMode.Locked)
            {
                ShowCursor(true);
            }

            ApplyCameraAngle();
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
        ///     Swing the camera back from overhead towards the players side by the configured angle, always aimed at the pivot
        /// </summary>
        private void ApplyCameraAngle()
        {
            var angle = Plugin.CameraAngle.Value;
            var radians = angle * Mathf.Deg2Rad;

            // The player stands on the -Z side of the table
            var offset = new Vector3(0f, Mathf.Cos(radians), -Mathf.Sin(radians)) * CameraDistance;
            Camera.transform.localPosition = CameraPivot + offset;
            Camera.transform.localRotation = Quaternion.Euler(90f - angle, 0f, 0f);
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
            if (Current != this)
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
