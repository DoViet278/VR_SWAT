using UnityEngine;
using System.Collections;
using System;
using UnityEngine.UI;

namespace KnifePlayerController
{
    public class WeaponUISelector : MonoBehaviour
    {
        [SerializeField]
        private PlayerHands hands;
        public UIHands[] UIHandsList;
        public CanvasGroup Group;
        public RectTransform ListRect;
        public RectTransform ViewRect;

        //public int TestSnap = -1;

        public float MoveDuration = 0.75f;
        public float FadeInDuration = 0.75f;
        public float FadeOutDuration = 2f;
        public float FadeOutDelay = 5f;

        public enum SelectorAnimationState
        {
            None,
            FadeIn,
            Select,
            FadeOut
        }

        public AnimationCurve FadeInCurve;
        public AnimationCurve MoveCurve;
        public AnimationCurve FadeOutCurve;

        [System.Serializable]
        public class UIHands
        {
            public string TargetHandsName;
            public RectTransform UIRoot;
            [HideInInspector]public HandsController TargetHands;
        }

        float targetYPosition;
        float selectedTime;
        float animationTime;
        float startPosition;

        RectTransform lastTargetRect;

        SelectorAnimationState currentState = SelectorAnimationState.None;

        public PlayerHands Hands
        {
            get
            {
                if (hands == null)
                {
                    hands = FindObjectOfType<PlayerHands>();
                }
                return hands;
            }

            set
            {
                hands = value;
            }
        }

        private void Start()
        {
            foreach(UIHands h in UIHandsList)
            {
                h.TargetHands = Hands.GetHands(h.TargetHandsName);

                if(h.TargetHands == null)
                {
                    Debug.LogError("No hands with name: " + h.TargetHandsName);
                    continue;
                }

                RectTransform target = h.UIRoot;
                h.TargetHands.DeployedEvent.AddListener(() =>
                    {
                        setTargetYPosition(target);
                    }
                );
                
                var uiHands = h;
                uiHands.UIRoot.gameObject.SetActive(uiHands.TargetHands.HandsItem.HasAny());
                h.TargetHands.HandsItem.ItemChanged.AddListener(() =>
                    {
                        uiHands.UIRoot.gameObject.SetActive(uiHands.TargetHands.HandsItem.HasAny());
                    }
                );
            }
        }

        //private void OnValidate()
        //{
        //    if(TestSnap >= 0 && TestSnap < UIHandsList.Length)
        //    {
        //        Vector2 pos = SnapTo(UIHandsList[TestSnap].UIRoot);
        //        Vector2 currentPosition = ListRect.anchoredPosition;
        //        currentPosition.y = pos.y;
        //        ListRect.anchoredPosition = currentPosition;
        //    }
        //}

        public Vector2 SnapTo(RectTransform target)
        {
            Canvas.ForceUpdateCanvases();

            return (Vector2)ViewRect.transform.InverseTransformPoint(ListRect.position) - (Vector2)ViewRect.transform.InverseTransformPoint(target.position);
        }

        void setTargetYPosition(RectTransform target)
        {
            lastTargetRect = target;
            targetYPosition = SnapTo(target).y;
            startPosition = ListRect.anchoredPosition.y;

            if(currentState == SelectorAnimationState.None)
            {
                currentState = SelectorAnimationState.FadeIn;
            } else if(currentState == SelectorAnimationState.FadeIn || currentState == SelectorAnimationState.FadeOut)
            {
                animationTime = 0;
                currentState = SelectorAnimationState.Select;

                Group.alpha = 1f;
            }
        }

        void checkTargetPosition()
        {
            if (lastTargetRect == null)
                return;

            Vector2 realTargetPosition = SnapTo(lastTargetRect);
            if(!Mathf.Approximately(targetYPosition, realTargetPosition.y))
            {
                targetYPosition = realTargetPosition.y;
                startPosition = ListRect.anchoredPosition.y;
            }
        }

        void Update()
        {
            checkTargetPosition();
            if (currentState == SelectorAnimationState.FadeIn)
            {
                animationTime += Time.deltaTime;
                float fraction = animationTime / FadeInDuration;
                float value = FadeInCurve.Evaluate(fraction);

                Group.alpha = value;

                if (fraction >= 1f)
                {
                    currentState = SelectorAnimationState.Select;
                    animationTime = 0;
                }
            } else if (currentState == SelectorAnimationState.Select)
            {
                animationTime += Time.deltaTime;
                float fraction = animationTime / MoveDuration;
                float value = MoveCurve.Evaluate(fraction);

                Vector2 currentPosition = ListRect.anchoredPosition;
                currentPosition.y = Mathf.LerpUnclamped(startPosition, targetYPosition, value);
                ListRect.anchoredPosition = currentPosition;

                if (fraction >= 1f)
                {
                    selectedTime = Time.time;
                    currentState = SelectorAnimationState.FadeOut;
                    animationTime = 0;
                }
            }
            else if(currentState == SelectorAnimationState.FadeOut)
            {
                if(Time.time >= selectedTime + FadeOutDelay)
                {
                    animationTime += Time.deltaTime;
                    float fraction = animationTime / FadeOutDuration;
                    float value = FadeOutCurve.Evaluate(fraction);

                    Group.alpha = value;

                    if (fraction >= 1f)
                    {
                        currentState = SelectorAnimationState.None;
                    }
                }
            }
        }
    }
}