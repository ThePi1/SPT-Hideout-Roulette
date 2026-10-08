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
        private const float PickDistance = 5f;
        private const float HighlightLift = 0.004f;

        public RouletteTable Table;
        public Camera Camera;
        public Light Lamp;
        public Transform Highlight;

        private HideoutPlayerOwner _owner;
        private int _chipIndex;
        private int _pickMask;
        private GUIStyle _hudStyle;

        /// <summary>
        ///     Table currently being played, null when the player is walking around
        /// </summary>
        public static TablePlayMode Current { get; private set; }

        private void Awake()
        {
            _pickMask = 1 << LayerMask.NameToLayer("Interactive");
        }

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
            ShowCursor(true);
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
            if (!Physics.Raycast(ray, out var hit, PickDistance, _pickMask))
            {
                return null;
            }

            var spot = hit.collider.GetComponent<BetSpot>();
            if (spot != null && spot.Table == Table)
            {
                return spot;
            }

            var wheelSpot = hit.collider.GetComponent<WheelSpot>();
            onWheel = wheelSpot != null && wheelSpot.Table == Table;

            return null;
        }

        private void UpdateHighlight(BetSpot spot)
        {
            Highlight.gameObject.SetActive(spot != null && !Table.Spinning);
            if (spot == null)
            {
                return;
            }

            var size = ((BoxCollider)spot.GetComponent<Collider>()).size;
            Highlight.localPosition = spot.transform.localPosition + Vector3.up * HighlightLift;
            Highlight.localScale = new Vector3(size.x, size.z, 1f);
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

            GUI.Box(new Rect(20f, 20f, 560f, 195f), text, _hudStyle);
        }

        private static void ShowCursor(bool visible)
        {
            Cursor.visible = visible;
            GClass3746.SetCursorLockMode(visible, Screen.fullScreenMode);
            GClass3746.SetCursor(visible ? ECursorType.Idle : ECursorType.Invisible);
        }
    }
}
