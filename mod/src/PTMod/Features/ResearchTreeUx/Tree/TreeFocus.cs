using System;
using System.Reflection;
using HarmonyLib;
using PTMod.Core;
using GameResearchButton = ProcessorTycoon.ResearchSystem.ResearchButton;
using GameTreeController = ProcessorTycoon.ResearchSystem.ResearchTreeController;
using UnityFindObjectsInactive = UnityEngine.FindObjectsInactive;
using UnityObject = UnityEngine.Object;
using UnityRectTransform = UnityEngine.RectTransform;
using UnityTransform = UnityEngine.Transform;
using UnityVector2 = UnityEngine.Vector2;
using UnityVector3 = UnityEngine.Vector3;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// Moves the view of the tree to a technology (FR-1 item 4): the game pans the tree by moving the rectangle
    /// <c>ResearchTreeController.controller</c> (a drag changes its <c>anchoredPosition</c>), so the focus moves the same rectangle. The shift is
    /// worked out in the world: the centre of the target button against the centre of the area that shows the tree (the rectangle of the
    /// controller component), turned into the space of the parent of the moved rectangle, so the zoom (<c>scaler</c>) and the scale of the canvas
    /// are taken into account without being read. Without the controller or its field the view stays where it is (design: "центрирование
    /// пропускается", the part is optional in <see cref="UxPartsRule"/>). Nothing here touches a value of the game besides that one position.
    /// </summary>
    internal static class TreeFocus
    {
        private static FieldInfo _controllerField;
        private static bool _fieldResolved;
        private static GameTreeController _controller;

        /// <summary>
        /// The object the tab strip is put under: the area that shows the tree (the one the controller component sits on), when it lies inside
        /// the window; otherwise the window itself. Null when there is no window.
        /// </summary>
        public static UnityTransform ViewportOf(TreeIndex tree)
        {
            if (tree == null || tree.Window == null) return null;

            GameTreeController controller = FindController(tree);
            UnityTransform viewport = controller == null ? null : controller.transform;
            if (viewport is UnityRectTransform && viewport.IsChildOf(tree.Window.transform)) return viewport;
            return tree.Window.transform;
        }

        /// <summary>Whether the view can be moved at all: the controller and its field are there.</summary>
        public static bool CanCenter(TreeIndex tree)
        {
            return MovedRectOf(FindController(tree)) != null;
        }

        /// <summary>
        /// Moves the view so that the button of the technology is in the middle of the area that shows the tree. False (and nothing moves) when
        /// the technology has no button, or the controller or its rectangle cannot be found.
        /// </summary>
        public static bool CenterOn(TreeIndex tree, int technologyId)
        {
            if (tree == null) return false;

            GameResearchButton button = tree.ButtonOf(technologyId);
            if (button == null) return false;

            GameTreeController controller = FindController(tree);
            UnityRectTransform moved = MovedRectOf(controller);
            var viewport = controller == null ? null : controller.transform as UnityRectTransform;
            var target = button.transform as UnityRectTransform;
            if (moved == null || viewport == null || target == null) return false;

            UnityVector3 targetWorld = target.TransformPoint(target.rect.center);
            UnityVector3 viewWorld = viewport.TransformPoint(viewport.rect.center);
            UnityVector3 shiftWorld = viewWorld - targetWorld;

            UnityTransform parent = moved.parent;
            UnityVector3 shift = parent == null ? shiftWorld : parent.InverseTransformVector(shiftWorld);
            moved.anchoredPosition += new UnityVector2(shift.x, shift.y);
            return true;
        }

        /// <summary>Forgets what was found (a new scene has another controller).</summary>
        public static void Reset()
        {
            _controller = null;
        }

        private static GameTreeController FindController(TreeIndex tree)
        {
            if (_controller != null) return _controller;   // Unity's comparison: a destroyed object is "null"

            try
            {
                _controller = UnityObject.FindFirstObjectByType<GameTreeController>(UnityFindObjectsInactive.Include);
            }
            catch (Exception e)
            {
                ModLog.Debug(ResearchTreeUxFeature.FeatureId, "focus: the tree controller cannot be looked up (" + e.GetType().Name + ")");
                _controller = null;
            }
            return _controller;
        }

        private static UnityRectTransform MovedRectOf(GameTreeController controller)
        {
            if (controller == null) return null;

            if (!_fieldResolved)
            {
                _fieldResolved = true;
                _controllerField = AccessTools.Field(typeof(GameTreeController), "controller");
            }
            if (_controllerField == null) return null;

            try
            {
                return _controllerField.GetValue(controller) as UnityRectTransform;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
