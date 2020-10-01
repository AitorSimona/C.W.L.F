﻿using Unity.Collections;
using Unity.Kinematica;
using Unity.Mathematics;
using Unity.SnapshotDebugger;
using UnityEngine;
using UnityEngine.Assertions;

namespace CWLF
{
    [RequireComponent(typeof(AbilityController))]
    [RequireComponent(typeof(MovementController))]

    public class ParkourAbility : SnapshotProvider, Ability
    {
        // --- Attributes ---
        [Header("Transition settings")]
        [Tooltip("Distance in meters for performing movement validity checks.")]
        [Range(0.0f, 1.0f)]
        public float contactThreshold;

        [Tooltip("Maximum linear error for transition poses.")]
        [Range(0.0f, 1.0f)]
        public float maximumLinearError;

        [Tooltip("Maximum angular error for transition poses.")]
        [Range(0.0f, 180.0f)]
        public float maximumAngularError;

        // -------------------------------------------------

        // TODO: Remove from here
        // --- Input wrapper ---
        public struct FrameCapture
        {
            public bool jumpButton;

            public void Update()
            {
                jumpButton = Input.GetButton("A Button");
            }
        }

        [Snapshot]
        FrameCapture capture;

        // -------------------------------------------------

        [Snapshot]
        AnchoredTransitionTask anchoredTransition; // Kinematica animation transition handler

        Kinematica kinematica;

        MovementController controller;

        // --- Basic Methods ---

        public override void OnEnable()
        {
            base.OnEnable();
            anchoredTransition = AnchoredTransitionTask.Invalid;
            controller = GetComponent<MovementController>();
            kinematica = GetComponent<Kinematica>();
        }

        public override void OnDisable()
        {
            base.OnDisable();
            anchoredTransition.Dispose();
        }

        public override void OnEarlyUpdate(bool rewind)
        {
            base.OnEarlyUpdate(rewind);

            if (!rewind) // if we are not using snapshot debugger to rewind
            {
                capture.Update();
            }
        }

        // -------------------------------------------------

        // --- Ability class methods ---
        public Ability OnUpdate(float deltaTime)
        {
            bool active = anchoredTransition.isValid;

            // --- If we are in a transition disable controller ---
            CollisionLayer.ConfigureController(active, ref controller);

            // TODO: Remove from here
            if (active)
            {
                ref MotionSynthesizer synthesizer = ref kinematica.Synthesizer.Ref;

                if (!anchoredTransition.IsState(AnchoredTransitionTask.State.Complete) && !anchoredTransition.IsState(AnchoredTransitionTask.State.Failed))
                {
                    anchoredTransition.synthesizer = MemoryRef<MotionSynthesizer>.Create(ref synthesizer);

                    // --- Finally tell kinematica to wait for this job to finish before executing other stuff ---
                    kinematica.AddJobDependency(AnchoredTransitionJob.Schedule(ref anchoredTransition));

                    return this;
                }

                anchoredTransition.Dispose();
                anchoredTransition = AnchoredTransitionTask.Invalid;
            }

            return null;
        }

        public bool OnContact(ref MotionSynthesizer synthesizer, AffineTransform contactTransform, float deltaTime)
        {
            bool ret = false;

            if (capture.jumpButton)
            {
                // --- Identify collider's object layer ---
                ref MovementController.Closure closure = ref controller.current;
                Assert.IsTrue(closure.isColliding);

                Collider collider = closure.collider;

                int layerMask = 1 << collider.gameObject.layer;
                Assert.IsTrue((layerMask & 0x1F01) != 0);

                Parkour type = Parkour.Create(collider.gameObject.layer);

                // Commented ISAxis queries so character is not limited when interacting with objects
                // MYTODO: these limits should be user defined

                if (type.IsType(Parkour.Type.Wall) || type.IsType(Parkour.Type.Table))
                {
                    //if (TagExtensions.IsAxis(collider, contactTransform, Missing.forward))
                    //{
                    ret = OnParkourContact(ref synthesizer, contactTransform, type);
                    //}
                }
                else if (type.IsType(Parkour.Type.Platform))
                {
                    //if (TagExtensions.IsAxis(collider, contactTransform, Missing.forward) ||
                    //    TagExtensions.IsAxis(collider, contactTransform, Missing.right))
                    //{
                    ret = OnParkourContact(ref synthesizer, contactTransform, type);
                    //}
                }
                else if (type.IsType(Parkour.Type.Ledge))
                {
                    //if (TagExtensions.IsAxis(collider, contactTransform, Missing.right))
                    //{
                    ret = OnParkourContact(ref synthesizer, contactTransform, type);
                    //}
                }
            }

            return ret;
        }

        bool OnParkourContact(ref MotionSynthesizer synthesizer, AffineTransform contactTransform, Parkour type)
        {
            // --- Get animation data of the type given (Parkour type) ---
            ref Binary binary = ref synthesizer.Binary;

            QueryResult sequence = TagExtensions.GetPoseSequence(ref binary, contactTransform,
                    type, contactThreshold);

            // --- Perform a transition to a 'type' tagged animation ---
            anchoredTransition.Dispose();
            anchoredTransition = AnchoredTransitionTask.Create(ref synthesizer,
                    sequence, contactTransform, maximumLinearError,
                        maximumAngularError);

            return true;
        }

        public bool OnDrop(ref MotionSynthesizer synthesizer, float deltaTime)
        {
            bool ret = false;

            if (controller.previous.isGrounded && controller.previous.ground != null)
            {
                // --- Get the ground's collider ---
                Transform ground = controller.previous.ground;
                BoxCollider collider = ground.GetComponent<BoxCollider>();

                if (collider != null)
                {
                    // --- Create all of the collider's vertices ---
                    NativeArray<float3> vertices = new NativeArray<float3>(4, Allocator.Persistent);

                    Vector3 center = collider.center;
                    Vector3 size = collider.size;

                    vertices[0] = ground.TransformPoint(center + new Vector3(-size.x, size.y, size.z) * 0.5f);
                    vertices[1] = ground.TransformPoint(center + new Vector3(size.x, size.y, size.z) * 0.5f);
                    vertices[2] = ground.TransformPoint(center + new Vector3(size.x, size.y, -size.z) * 0.5f);
                    vertices[3] = ground.TransformPoint(center + new Vector3(-size.x, size.y, -size.z) * 0.5f);

                    float3 p = controller.previous.position;
                    AffineTransform contactTransform = TagExtensions.GetClosestTransform(vertices[0], vertices[1], p);
                    float minimumDistance = math.length(contactTransform.t - p);

                    // --- Find out where the character will make contact with the ground ---
                    for (int i = 1; i < 4; ++i)
                    {
                        int j = (i + 1) % 4;
                        AffineTransform candidateTransform = TagExtensions.GetClosestTransform(vertices[i], vertices[j], p);

                        float distance = math.length(candidateTransform.t - p);
                        if (distance < minimumDistance)
                        {
                            minimumDistance = distance;
                            contactTransform = candidateTransform;
                        }
                    }

                    vertices.Dispose();

                    // --- Activate a transition towards the contact point ---
                    ret = OnParkourContact(ref synthesizer, contactTransform, Parkour.Create(Parkour.Type.DropDown));
                }
            }

            return ret;
        }

        // -------------------------------------------------
    }
}
