using UnityEngine;
using UnityEngine.InputSystem;

namespace TheDungeon.Game
{
    /// <summary>
    /// Entrées du jeu (Input System). Les touches sont repérées par leur position physique :
    /// ZQSD sur un clavier AZERTY correspond à WASD sur un QWERTY. Manette prise en charge.
    /// </summary>
    public static class GameInput
    {
        public static Vector2 Move
        {
            get
            {
                var move = Vector2.zero;
                var keyboard = Keyboard.current;
                if (keyboard != null)
                {
                    if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1f;
                    if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1f;
                    if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1f;
                    if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1f;
                }
                if (Gamepad.current != null)
                    move += Gamepad.current.leftStick.ReadValue();
                return Vector2.ClampMagnitude(move, 1f);
            }
        }

        /// <summary>Direction de visée depuis une position à l'écran : souris, ou stick droit de la manette.</summary>
        public static Vector2 AimFrom(Vector2 screenPosition, Vector2 fallback)
        {
            if (Gamepad.current != null)
            {
                var stick = Gamepad.current.rightStick.ReadValue();
                if (stick.sqrMagnitude > 0.2f)
                    return stick.normalized;
            }
            if (Mouse.current == null)
                return fallback;
            var direction = Mouse.current.position.ReadValue() - screenPosition;
            return direction.sqrMagnitude > 1f ? direction.normalized : fallback;
        }

        public static bool Fire =>
            (Mouse.current != null && Mouse.current.leftButton.isPressed) ||
            (Gamepad.current != null && Gamepad.current.rightTrigger.isPressed);

        public static bool Dash => Pressed(Key.Space) || Pressed(Key.LeftShift) || (Gamepad.current?.buttonEast.wasPressedThisFrame ?? false);
        public static bool Interact => Pressed(Key.E) || (Gamepad.current?.buttonSouth.wasPressedThisFrame ?? false);
        public static bool UsePotion => Pressed(Key.F) || (Gamepad.current?.buttonNorth.wasPressedThisFrame ?? false);
        public static bool Inventory => Pressed(Key.I) || Pressed(Key.Tab) || (Gamepad.current?.selectButton.wasPressedThisFrame ?? false);
        public static bool Pause => Pressed(Key.Escape) || (Gamepad.current?.startButton.wasPressedThisFrame ?? false);
        public static bool Confirm => Pressed(Key.Enter) || Pressed(Key.NumpadEnter) || Pressed(Key.Space) ||
                                      (Gamepad.current?.buttonSouth.wasPressedThisFrame ?? false);
        public static bool Restart => Pressed(Key.R);
        public static bool Click => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        /// <summary>Touche 1 à 4 (choix de dialogue) : renvoie l'index, ou -1.</summary>
        public static int Choice
        {
            get
            {
                var keys = new[] { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4 };
                var numpad = new[] { Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4 };
                for (var i = 0; i < keys.Length; i++)
                    if (Pressed(keys[i]) || Pressed(numpad[i]))
                        return i;
                return -1;
            }
        }

        static bool Pressed(Key key) => Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;
    }
}
