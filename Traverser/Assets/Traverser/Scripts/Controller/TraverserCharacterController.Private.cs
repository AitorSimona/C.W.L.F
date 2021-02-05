using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Traverser
{
    public partial class TraverserCharacterController : MonoBehaviour
    {
        public struct TraverserCollision
        {
            // The collider we just made contact with
            public Collider collider;

            // Sattes if we are currently colliding
            public bool isColliding;

            // The point at which we collided with the current collider
            public float3 colliderContactPoint;

            // The current collider's normal direction 
            public float3 colliderContactNormal;

            // Transform of the current ground, the object below the character
            public Transform ground;

            internal static TraverserCollision Create()
            {
                return new TraverserCollision()
                {
                    collider = null,
                    isColliding = false,
                    colliderContactNormal = float3.zero,
                    colliderContactPoint = float3.zero,
                    ground = null
                };
            }

            internal void CopyFrom(ref TraverserCollision copyCollision)
            {
                collider = copyCollision.collider;
                isColliding = copyCollision.isColliding;
                colliderContactPoint = copyCollision.colliderContactPoint;
                colliderContactNormal = copyCollision.colliderContactNormal;
                ground = copyCollision.ground;
            }
        }

        public struct TraverserState
        {
            public TraverserCollision previousCollision;
            public TraverserCollision currentCollision;
            public Transform transform;

            internal static TraverserState Create()
            {
                return new TraverserState()
                {
                    previousCollision = TraverserCollision.Create(),
                    currentCollision = TraverserCollision.Create()
                };
            }

            internal void CopyFrom(ref TraverserState copyState)
            {
                previousCollision.CopyFrom(ref copyState.previousCollision);
                currentCollision.CopyFrom(ref copyState.currentCollision);
                transform = copyState.transform;
            }
        }

        [HideInInspector]
        private CharacterController characterController;

        private TraverserState state;
        private TraverserState snapshotState;

        // Start is called before the first frame update
        void Start()
        {
            state = TraverserState.Create();
            snapshotState = TraverserState.Create();
            characterController = GetComponent<CharacterController>();
            state.transform = characterController.transform;
            snapshotState.transform = characterController.transform;
        }

        // Update is called once per frame
        void Update()
        {

        }
    }

}