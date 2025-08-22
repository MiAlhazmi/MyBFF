using System;
using UnityEngine;

namespace MyBFF.Voice
{
    [RequireComponent(typeof(Collider))]
    public class ConversationZone : MonoBehaviour
    {
        public string playerTag = "Player";
        public bool autoStartOnEnter = true;
        public bool autoStopOnExit = true;

        private ElevenLabsVoiceChat voiceChat;

        private void Awake()
        {
            voiceChat = FindObjectOfType<ElevenLabsVoiceChat>();
        }

        void Reset()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(playerTag)) return;
            if (autoStartOnEnter) voiceChat.StartConversation();
            // if (autoStartOnEnter) ConversationManager.Instance?.BeginConversation();
        }

        void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag(playerTag)) return;
            if (autoStopOnExit) voiceChat.StopConversation();
            // if (autoStopOnExit) ConversationManager.Instance?.EndConversation();
        }
    }
}