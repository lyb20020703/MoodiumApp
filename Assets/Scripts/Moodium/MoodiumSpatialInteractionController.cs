using System.Collections.Generic;
using Unity.PolySpatial.InputDevices;
using Moodium.Interaction;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.LowLevel;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using Moodium.Flow.ObjectPicker;
using Moodium.Calibration;
using Moodium.MoodCheck;
using Moodium.UI;

namespace Moodium.Flow
{
    public sealed class MoodiumSpatialInteractionController : MonoBehaviour
    {
        readonly Dictionary<int, MoodiumManipulable> m_Selections = new();
        readonly Dictionary<int, MoodiumObjectInteraction> m_PlayInteractions = new();
        readonly Dictionary<int, WorldCarouselController> m_CarouselInteractions = new();
        readonly Dictionary<int, ChocolateCapsuleInteraction> m_ChocolateInteractions = new();
        readonly Dictionary<int, MoodiumWindowHandle> m_WindowInteractions = new();
        readonly Dictionary<int, SensoryBagCalibrationHandle> m_CalibrationInteractions = new();
        readonly Dictionary<int, MoodCheckPlateHandle> m_MoodCheckInteractions = new();
        readonly Dictionary<int, GlassSlideToEnter> m_GlassSliderInteractions = new();
        readonly Dictionary<int, Moodium.Guide.WorldGuideSwipeHandle> m_GuideSwipeInteractions=new();
        readonly Dictionary<int, WorldCarouselSwipeHandle> m_BranchCarouselInteractions = new();
        readonly Dictionary<int, Moodium.NatureWorld.BubbleDoorHandle> m_DoorInteractions = new();
        readonly HashSet<MoodiumSpatialButton> m_HoveredButtons = new();
        readonly HashSet<MoodiumSpatialButton> m_ButtonsThisFrame = new();
        readonly HashSet<MoodiumObjectPickerItem> m_HoveredPickerItems = new();
        readonly HashSet<MoodiumObjectPickerItem> m_PickerItemsThisFrame = new();

#if UNITY_EDITOR
        const int MousePointerId = int.MaxValue;
        MoodCheckPlateHandle m_MousePlate;
        Moodium.Guide.WorldGuideSwipeHandle m_MouseGuide;
        SensoryBagCalibrationHandle m_MouseCalibration;
        WorldCarouselSwipeHandle m_MouseBranchCarousel;
        GlassSlideToEnter m_MouseSlider;
        Moodium.NatureWorld.BubbleDoorHandle m_MouseDoor;
        Plane m_MouseDragPlane;

        void UpdateEditorMouse()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            var camera = Camera.main;
            if (mouse == null || camera == null) return;
            var ray = camera.ScreenPointToRay(mouse.position.ReadValue());
            if (m_MouseDoor != null)
            {
                // The mercury door slides over the floor: drag on a horizontal plane through the grab point.
                if (m_MouseDragPlane.Raycast(ray, out var doorDistance)) m_MouseDoor.PointerMoved(MousePointerId, ray.GetPoint(doorDistance));
                if (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed)
                {
                    var door = m_MouseDoor; m_MouseDoor = null;
                    door.PointerEnded(MousePointerId);
                }
                return;
            }
            if (m_MouseBranchCarousel != null || m_MouseSlider != null)
            {
                if (m_MouseDragPlane.Raycast(ray, out var dragDistance))
                {
                    var point = ray.GetPoint(dragDistance);
                    if (m_MouseBranchCarousel != null) m_MouseBranchCarousel.PointerMoved(MousePointerId, point);
                    if (m_MouseSlider != null) m_MouseSlider.PointerMoved(MousePointerId, point);
                }
                if (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed)
                {
                    var carousel = m_MouseBranchCarousel; var slider = m_MouseSlider;
                    m_MouseBranchCarousel = null; m_MouseSlider = null;
                    if (carousel != null) carousel.PointerEnded(MousePointerId);
                    if (slider != null) slider.PointerEnded(MousePointerId);
                }
                return;
            }
            if(m_MouseCalibration!=null)
            {
                if(!m_MouseCalibration.CanInteract)
                {
                    m_MouseCalibration.PointerEnded(MousePointerId);m_MouseCalibration=null;return;
                }
                if(m_MouseDragPlane.Raycast(ray,out var calibrationDistance))
                    m_MouseCalibration.PointerMoved(MousePointerId,ray.GetPoint(calibrationDistance));
                if(mouse.leftButton.wasReleasedThisFrame||!mouse.leftButton.isPressed)
                {
                    m_MouseCalibration.PointerEnded(MousePointerId);m_MouseCalibration=null;
                }
                return;
            }
            if(m_MouseGuide!=null)
            {
                if(!m_MouseGuide.isActiveAndEnabled)
                {
                    m_MouseGuide.CancelPointer(MousePointerId);m_MouseGuide=null;return;
                }
                if(m_MouseDragPlane.Raycast(ray,out var guideDistance))
                {
                    m_MouseGuide.PointerMoved(MousePointerId,ray.GetPoint(guideDistance));
                    if(mouse.leftButton.wasReleasedThisFrame||!mouse.leftButton.isPressed)
                    {
                        var guide=m_MouseGuide;m_MouseGuide=null;guide.PointerEnded(MousePointerId);
                    }
                }
                else if(!mouse.leftButton.isPressed)
                {
                    m_MouseGuide.CancelPointer(MousePointerId);m_MouseGuide=null;
                }
                return;
            }
            if (m_MousePlate != null)
            {
                if (!m_MousePlate.isActiveAndEnabled)
                {
                    m_MousePlate.CancelPointer(MousePointerId);
                    m_MousePlate = null;
                    return;
                }
                if (m_MouseDragPlane.Raycast(ray, out var distance))
                {
                    var point = ray.GetPoint(distance);
                    m_MousePlate.PointerMoved(MousePointerId, point);
                    if (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed)
                    {
                        var plate = m_MousePlate;
                        m_MousePlate = null;
                        plate.PointerEnded(MousePointerId, point);
                    }
                }
                else if (!mouse.leftButton.isPressed)
                {
                    m_MousePlate.CancelPointer(MousePointerId);
                    m_MousePlate = null;
                }
                return;
            }
            // uGUI language/video buttons retain their existing EventSystem input.
            if (!mouse.leftButton.wasPressedThisFrame || Touch.activeTouches.Count > 0) return;
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
            if (!Physics.Raycast(ray, out var hit, 20f, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Collide)) return;
            var doorHandle = hit.collider.GetComponentInParent<Moodium.NatureWorld.BubbleDoorHandle>();
            if (doorHandle != null && doorHandle.CanInteract)
            {
                m_MouseDoor = doorHandle;
                m_MouseDragPlane = new Plane(Vector3.up, hit.point);
                doorHandle.PointerStarted(MousePointerId, hit.point); return;
            }
            var button = hit.collider.GetComponentInParent<MoodiumSpatialButton>();
            var branchCarousel = hit.collider.GetComponentInParent<WorldCarouselSwipeHandle>();
            var branchSlider = hit.collider.GetComponentInParent<GlassSlideToEnter>();
            if (branchSlider != null)
            {
                m_MouseSlider = branchSlider;
                m_MouseDragPlane = new Plane(branchSlider.transform.forward, hit.point);
                branchSlider.PointerStarted(MousePointerId, hit.point); return;
            }
            if (branchCarousel != null && branchCarousel.CanInteract)
            {
                m_MouseBranchCarousel = branchCarousel;
                m_MouseDragPlane = new Plane(branchCarousel.transform.forward, hit.point);
                branchCarousel.PointerStarted(MousePointerId, hit.point); return;
            }
            var calibration=hit.collider.GetComponentInParent<SensoryBagCalibrationHandle>();
            if(calibration!=null&&calibration.CanInteract)
            {
                if(calibration.IsDragHandle)
                {
                    m_MouseCalibration=calibration;
                    m_MouseDragPlane=new Plane(calibration.IsHeightHandle?camera.transform.forward:calibration.FrameNormal,hit.point);
                }
                calibration.PointerStarted(MousePointerId,hit.point);return;
            }
            if (button != null && button.isActiveAndEnabled)
            {
                button.Press();
                return;
            }
            var handle = hit.collider.GetComponentInParent<MoodCheckPlateHandle>();
            var guideHandle=hit.collider.GetComponentInParent<Moodium.Guide.WorldGuideSwipeHandle>();
            if(guideHandle!=null&&guideHandle.CanInteract)
            {
                m_MouseGuide=guideHandle;m_MouseDragPlane=new Plane(guideHandle.transform.forward,hit.point);
                guideHandle.PointerStarted(MousePointerId,hit.point);return;
            }
            if (handle != null && handle.isActiveAndEnabled)
            {
                m_MousePlate = handle;
                m_MouseDragPlane = new Plane(handle.transform.forward, hit.point);
                handle.PointerStarted(MousePointerId, hit.point);
            }
        }

#endif

        void OnDisable()
        {
#if UNITY_EDITOR
            if (m_MouseBranchCarousel != null) m_MouseBranchCarousel.CancelPointer(MousePointerId);
            if (m_MouseSlider != null) m_MouseSlider.CancelPointer(MousePointerId);
            m_MouseBranchCarousel = null; m_MouseSlider = null;
            if (m_MouseDoor != null) m_MouseDoor.CancelPointer(MousePointerId);
            m_MouseDoor = null;
            if(m_MousePlate!=null)m_MousePlate.CancelPointer(MousePointerId);
            if(m_MouseGuide!=null)m_MouseGuide.CancelPointer(MousePointerId);
            if(m_MouseCalibration!=null)m_MouseCalibration.PointerEnded(MousePointerId);
            m_MouseCalibration=null;
            m_MousePlate=null;m_MouseGuide=null;
#endif
            foreach (var pair in m_BranchCarouselInteractions)
                if (pair.Value != null) pair.Value.CancelPointer(pair.Key);
            m_BranchCarouselInteractions.Clear();
            foreach (var pair in m_DoorInteractions)
                if (pair.Value != null) pair.Value.CancelPointer(pair.Key);
            m_DoorInteractions.Clear();
            foreach (var pair in m_GlassSliderInteractions)
                if (pair.Value != null) pair.Value.CancelPointer(pair.Key);
            m_GlassSliderInteractions.Clear();
            foreach(var pair in m_GuideSwipeInteractions)
                if(pair.Value!=null)pair.Value.CancelPointer(pair.Key);
            m_GuideSwipeInteractions.Clear();
            foreach(var pair in m_CalibrationInteractions)
                if(pair.Value!=null)pair.Value.PointerEnded(pair.Key);
            m_CalibrationInteractions.Clear();
        }

        void OnEnable()
        {
            EnhancedTouchSupport.Enable();
        }

        void Update()
        {
#if UNITY_EDITOR
            UpdateEditorMouse();
#endif
            m_ButtonsThisFrame.Clear();
            m_PickerItemsThisFrame.Clear();
            foreach (var touch in Touch.activeTouches)
            {
                var pointer = EnhancedSpatialPointerSupport.GetPointerState(touch);
                var id = pointer.interactionId;
                var hoverButton = pointer.targetObject != null
                    ? pointer.targetObject.GetComponentInParent<MoodiumSpatialButton>()
                    : null;
                if (hoverButton != null)
                    m_ButtonsThisFrame.Add(hoverButton);
                var hoverPickerItem = pointer.targetObject != null
                    ? pointer.targetObject.GetComponentInParent<MoodiumObjectPickerItem>()
                    : null;
                if (hoverPickerItem != null)
                    m_PickerItemsThisFrame.Add(hoverPickerItem);

                if (pointer.phase == SpatialPointerPhase.Began)
                {
                    var target = pointer.targetObject;
                    var doorHandle = target != null
                        ? target.GetComponentInParent<Moodium.NatureWorld.BubbleDoorHandle>() : null;
                    if (doorHandle != null && doorHandle.TryOpenParked()) continue;
                    // Reuse the system's gaze+pinch Began event. Never use head-forward as gaze,
                    // and never route a UI/model gesture through to the room behind it.
                    if (pointer.Kind == SpatialPointerKind.IndirectPinch)
                    {
                        var ivy = FindFirstObjectByType<Moodium.NatureWorld.NatureIvySpatialGrowthController>();
                        var collider = target != null ? target.GetComponent<Collider>() : null;
                        // The room mesh reaches visionOS only through the vine controller's Default-layer gaze targets.
                        if (ivy != null && ivy.EnvironmentUnlocked && (target == null || ivy.IsSpatialMeshCollider(collider)))
                        {
                            var hasRay = pointer.startInteractionRayDirection.sqrMagnitude > .000001f;
                            // Recover a droplet hit before offering this same pinch to vines.
                            if (hasRay && Physics.Raycast(pointer.startInteractionRayOrigin,
                                pointer.startInteractionRayDirection, out var doorHit, 8f, 1))
                            {
                                var parkedHandle = doorHit.collider.GetComponentInParent<Moodium.NatureWorld.BubbleDoorHandle>();
                                if (parkedHandle != null && parkedHandle.TryOpenParked()) continue;
                            }
                            var plantable = ivy.CanPlantEnvironment;
                            var grown = false;
                            if (plantable)
                            {
                                if (hasRay)
                                    grown = ivy.TryGrowFromGazeRay(new Ray(pointer.startInteractionRayOrigin,
                                        pointer.startInteractionRayDirection));
                                else if (collider != null)
                                    grown = ivy.TryGrowFromSpatialPointer(collider, pointer.interactionPosition);
                            }
                            ivy.NotePinch(target, hasRay, plantable, grown);
                            if (plantable) continue;
                        }
                    }
                    if (target != null)
                    {
                        var branchCarousel = target.GetComponentInParent<WorldCarouselSwipeHandle>();
                        if (branchCarousel != null)
                        {
                            if (branchCarousel.CanInteract)
                            {
                                m_BranchCarouselInteractions[id] = branchCarousel;
                                branchCarousel.PointerStarted(id, pointer.interactionPosition);
                            }
                            continue;
                        }
                        // The mercury door's ring: a pinch that moves drags the door over the floor, one that stays put is a tap.
                        if (doorHandle != null)
                        {
                            if (doorHandle.CanInteract)
                            {
                                m_DoorInteractions[id] = doorHandle;
                                doorHandle.PointerStarted(id, pointer.interactionPosition);
                            }
                            continue;
                        }
                        var guideSwipe=target.GetComponentInParent<Moodium.Guide.WorldGuideSwipeHandle>();
                        if(guideSwipe!=null)
                        {
                            if(guideSwipe.CanInteract)
                            {
                                m_GuideSwipeInteractions[id]=guideSwipe;
                                guideSwipe.PointerStarted(id,pointer.interactionPosition);
                            }
                            continue;
                        }
                        var pickerItem = target.GetComponentInParent<MoodiumObjectPickerItem>();
                        if (pickerItem != null)
                        {
                            pickerItem.Press();
                            continue;
                        }

                        var calibrationHandle = target.GetComponentInParent<SensoryBagCalibrationHandle>();
                        if (calibrationHandle != null)
                        {
                            m_CalibrationInteractions[id] = calibrationHandle;
                            calibrationHandle.PointerStarted(id, pointer.interactionPosition);
                            continue;
                        }

                        var moodCheckHandle = target.GetComponentInParent<MoodCheckPlateHandle>();
                        if (moodCheckHandle != null)
                        {
                            m_MoodCheckInteractions[id] = moodCheckHandle;
                            moodCheckHandle.PointerStarted(id, pointer.interactionPosition);
                            continue;
                        }

                        var windowHandle = target.GetComponentInParent<MoodiumWindowHandle>();
                        if (windowHandle != null)
                        {
                            m_WindowInteractions[id] = windowHandle;
                            windowHandle.PointerStarted(id, pointer.interactionPosition);
                            continue;
                        }

                        var chocolate = target.GetComponentInParent<ChocolateCapsuleInteraction>();
                        if (chocolate != null && chocolate.InteractionEnabled && chocolate.SpatialPointerEnabled)
                        {
                            m_ChocolateInteractions[id] = chocolate;
                            chocolate.PointerStarted(id, pointer.interactionPosition);
                            continue;
                        }

                        var worldItem = target.GetComponentInParent<WorldCarouselItem>();
                        var carousel = worldItem != null
                            ? worldItem.GetComponentInParent<WorldCarouselController>()
                            : null;
                        if (carousel != null)
                        {
                            m_CarouselInteractions[id] = carousel;
                            carousel.PointerStarted(id, worldItem, pointer.interactionPosition);
                            continue;
                        }

                        var button = target.GetComponentInParent<MoodiumSpatialButton>();
                        var glassSlider = target.GetComponentInParent<GlassSlideToEnter>();
                        if (glassSlider != null)
                        {
                            m_GlassSliderInteractions[id] = glassSlider;
                            glassSlider.PointerStarted(id, pointer.interactionPosition);
                            continue;
                        }
                        if (button != null)
                        {
                            button.Press();
                            continue;
                        }

                        var manipulable = target.GetComponentInParent<MoodiumManipulable>();
                        if (manipulable != null && manipulable.CanManipulate)
                        {
                            m_Selections[id] = manipulable;
                            manipulable.BeginPointer(
                                id,
                                pointer.interactionPosition,
                                pointer.inputDeviceRotation);
                        }
                        else
                        {
                            var interaction = target.GetComponentInParent<MoodiumObjectInteraction>();
                            if (interaction != null && interaction.InteractionEnabled)
                            {
                                m_PlayInteractions[id] = interaction;
                                interaction.PointerStarted(id, pointer.interactionPosition);
                            }
                        }
                    }
                }

                if (m_DoorInteractions.TryGetValue(id, out var doorInteraction))
                {
                    if (doorInteraction == null) { m_DoorInteractions.Remove(id); continue; }
                    if (pointer.phase == SpatialPointerPhase.Moved) doorInteraction.PointerMoved(id, pointer.interactionPosition);
                    else if (pointer.phase == SpatialPointerPhase.Ended)
                    { doorInteraction.PointerEnded(id); m_DoorInteractions.Remove(id); }
                    else if (pointer.phase == SpatialPointerPhase.Cancelled || pointer.phase == SpatialPointerPhase.None)
                    { doorInteraction.CancelPointer(id); m_DoorInteractions.Remove(id); }
                    continue;
                }

                if (m_BranchCarouselInteractions.TryGetValue(id, out var branchInteraction))
                {
                    if (branchInteraction == null) { m_BranchCarouselInteractions.Remove(id); continue; }
                    if (pointer.phase == SpatialPointerPhase.Moved) branchInteraction.PointerMoved(id, pointer.interactionPosition);
                    else if (pointer.phase == SpatialPointerPhase.Ended)
                    { branchInteraction.PointerEnded(id); m_BranchCarouselInteractions.Remove(id); }
                    else if (pointer.phase == SpatialPointerPhase.Cancelled || pointer.phase == SpatialPointerPhase.None)
                    { branchInteraction.CancelPointer(id); m_BranchCarouselInteractions.Remove(id); }
                    continue;
                }

                if(m_GuideSwipeInteractions.TryGetValue(id,out var guideSwipeInteraction))
                {
                    if(guideSwipeInteraction==null){m_GuideSwipeInteractions.Remove(id);continue;}
                    if(pointer.phase==SpatialPointerPhase.Moved)
                        guideSwipeInteraction.PointerMoved(id,pointer.interactionPosition);
                    else if(pointer.phase==SpatialPointerPhase.Ended)
                    {
                        guideSwipeInteraction.PointerEnded(id);m_GuideSwipeInteractions.Remove(id);
                    }
                    else if(pointer.phase==SpatialPointerPhase.Cancelled||pointer.phase==SpatialPointerPhase.None)
                    {
                        guideSwipeInteraction.CancelPointer(id);m_GuideSwipeInteractions.Remove(id);
                    }
                    continue;
                }

                if (m_GlassSliderInteractions.TryGetValue(id, out var glassSliderInteraction))
                {
                    if (pointer.phase == SpatialPointerPhase.Moved)
                        glassSliderInteraction.PointerMoved(id, pointer.interactionPosition);
                    else if (pointer.phase == SpatialPointerPhase.Ended)
                    {
                        glassSliderInteraction.PointerEnded(id);
                        m_GlassSliderInteractions.Remove(id);
                    }
                    else if (pointer.phase == SpatialPointerPhase.Cancelled || pointer.phase == SpatialPointerPhase.None)
                    {
                        glassSliderInteraction.CancelPointer(id);
                        m_GlassSliderInteractions.Remove(id);
                    }
                    continue;
                }

                if (m_ChocolateInteractions.TryGetValue(id, out var chocolateInteraction))
                {
                    if (pointer.phase == SpatialPointerPhase.Ended ||
                        pointer.phase == SpatialPointerPhase.Cancelled ||
                        pointer.phase == SpatialPointerPhase.None)
                    {
                        chocolateInteraction.PointerEnded(id);
                        m_ChocolateInteractions.Remove(id);
                    }
                    continue;
                }

                if (m_CalibrationInteractions.TryGetValue(id, out var calibrationInteraction))
                {
                    switch (pointer.phase)
                    {
                        case SpatialPointerPhase.Moved:
                            calibrationInteraction.PointerMoved(id, pointer.interactionPosition);
                            break;
                        case SpatialPointerPhase.Ended:
                        case SpatialPointerPhase.Cancelled:
                        case SpatialPointerPhase.None:
                            calibrationInteraction.PointerEnded(id);
                            m_CalibrationInteractions.Remove(id);
                            break;
                    }
                    continue;
                }

                if (m_MoodCheckInteractions.TryGetValue(id, out var moodCheckInteraction))
                {
                    switch (pointer.phase)
                    {
                        case SpatialPointerPhase.Moved:
                            moodCheckInteraction.PointerMoved(id, pointer.interactionPosition);
                            break;
                        case SpatialPointerPhase.Ended:
                            moodCheckInteraction.PointerEnded(id, pointer.interactionPosition);
                            m_MoodCheckInteractions.Remove(id);
                            break;
                        case SpatialPointerPhase.Cancelled:
                        case SpatialPointerPhase.None:
                            moodCheckInteraction.CancelPointer(id);
                            m_MoodCheckInteractions.Remove(id);
                            break;
                    }
                    continue;
                }

                if (m_WindowInteractions.TryGetValue(id, out var windowInteraction))
                {
                    switch (pointer.phase)
                    {
                        case SpatialPointerPhase.Moved:
                            windowInteraction.PointerMoved(id, pointer.interactionPosition);
                            break;
                        case SpatialPointerPhase.Ended:
                        case SpatialPointerPhase.Cancelled:
                        case SpatialPointerPhase.None:
                            windowInteraction.PointerEnded(id);
                            m_WindowInteractions.Remove(id);
                            break;
                    }
                    continue;
                }

                if (m_CarouselInteractions.TryGetValue(id, out var carouselInteraction))
                {
                    switch (pointer.phase)
                    {
                        case SpatialPointerPhase.Moved:
                            carouselInteraction.PointerMoved(id, pointer.interactionPosition);
                            break;
                        case SpatialPointerPhase.Ended:
                        case SpatialPointerPhase.Cancelled:
                        case SpatialPointerPhase.None:
                            carouselInteraction.PointerEnded(id);
                            m_CarouselInteractions.Remove(id);
                            break;
                    }
                    continue;
                }

                if (m_PlayInteractions.TryGetValue(id, out var playInteraction) &&
                    pointer.phase == SpatialPointerPhase.Moved)
                {
                    playInteraction.PointerMoved(id, pointer.interactionPosition);
                }
                else if (m_PlayInteractions.TryGetValue(id, out playInteraction) &&
                         (pointer.phase == SpatialPointerPhase.Ended ||
                          pointer.phase == SpatialPointerPhase.Cancelled ||
                          pointer.phase == SpatialPointerPhase.None))
                {
                    playInteraction.PointerEnded(id);
                    m_PlayInteractions.Remove(id);
                }

                if (!m_Selections.TryGetValue(id, out var selection))
                    continue;

                switch (pointer.phase)
                {
                    case SpatialPointerPhase.Moved:
                        selection.UpdatePointer(
                            id,
                            pointer.interactionPosition,
                            pointer.inputDeviceRotation);
                        break;
                    case SpatialPointerPhase.Ended:
                    case SpatialPointerPhase.Cancelled:
                    case SpatialPointerPhase.None:
                        selection.EndPointer(id);
                        m_Selections.Remove(id);
                        break;
                }
            }

            foreach (var button in m_HoveredButtons)
                if (button != null && !m_ButtonsThisFrame.Contains(button))
                    button.SetHovered(false);
            foreach (var button in m_ButtonsThisFrame)
                if (button != null)
                    button.SetHovered(true);
            m_HoveredButtons.Clear();
            foreach (var button in m_ButtonsThisFrame)
                m_HoveredButtons.Add(button);
            foreach (var item in m_HoveredPickerItems)
                if (item != null && !m_PickerItemsThisFrame.Contains(item))
                    item.SetHovered(false);
            foreach (var item in m_PickerItemsThisFrame)
                if (item != null)
                    item.SetHovered(true);
            m_HoveredPickerItems.Clear();
            foreach (var item in m_PickerItemsThisFrame)
                m_HoveredPickerItems.Add(item);
        }
    }
}
