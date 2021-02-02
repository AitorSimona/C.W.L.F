﻿using Unity.Kinematica;
using Unity.Mathematics;
using Unity.SnapshotDebugger;
using UnityEngine;

// --- Wrapper for ability-input interactions ---

namespace CWLF
{
    public static class InputLayer
    {
        public struct FrameCapture
        {
            // --- Attributes ---
            public float3 movementDirection;
            public float moveIntensity;
            public bool run;
            public bool dropDownButton;
            // --------------------------------

            public bool parkourButton;
            public bool parkourDropDownButton;

            // --------------------------------

            public float stickHorizontal;
            public float stickVertical;
            public bool mountButton;
            public bool dismountButton;
            public bool pullUpButton;

            // --------------------------------

            // --- Basic methods ---

            // NOTE: Careful with overlapping input!! Another action may be activated due to sharing
            // the same input, and you won't notice it.

            public void UpdateLocomotion()
            {
                Utility.GetInputMove(ref movementDirection, ref moveIntensity);
                run = Input.GetButton("Left Analog Button");
            }

            public void UpdateParkour()
            {
                parkourButton = Input.GetButton("A Button") || Input.GetKey("a");
                parkourDropDownButton = Input.GetButton("C Button") || Input.GetKey("c");
            }

            public void UpdateClimbing()
            {
                stickHorizontal = Input.GetAxis("Left Analog Horizontal");
                stickVertical = Input.GetAxis("Left Analog Vertical");

                //Debug.Log(stickVertical);
                mountButton = Input.GetButton("B Button") || Input.GetKey("b");
                dropDownButton = Input.GetButton("A Button") || Input.GetKey("a");
                dismountButton = Input.GetButton("B Button") || Input.GetKey("b");
                pullUpButton = Input.GetButton("A Button") || Input.GetKey("a");
            }

            // --------------------------------
        }

        // --- Attributes ---
        [Snapshot]
        public static FrameCapture capture;

        // --------------------------------

        // --- Utilities ---
        public static float2 GetStickInput()
        {
            float2 stickInput;
            stickInput.x = capture.stickHorizontal;
            stickInput.y = capture.stickVertical;

            if (math.length(stickInput) >= 0.1f)
            {
                if (math.length(stickInput) > 1.0f)
                    stickInput = math.normalize(stickInput);
            }
            else
                stickInput = float2.zero;

            return stickInput;
        }

        // --------------------------------
    }
}
