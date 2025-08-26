# Lulu - AI Voice Companion for Children

A Unity-based interactive game where children (ages 6-10) can have real-time voice conversations with Lulu, an AI companion character.

## Overview

This project creates an immersive environment where children can walk around a playroom and engage in natural voice conversations with Lulu, an AI character. The system provides real-time speech recognition, AI responses, and text-to-speech synthesis through ElevenLabs Conversational AI integration.

## Core Features

### Voice Interaction System
- **Real-time voice chat** with ElevenLabs Conversational AI WebSocket integration
- **Bidirectional audio streaming** - children speak to Lulu and hear her responses
- **Natural conversation flow** with proper turn-taking and interruption handling
- **Audio buffering system** prevents dropouts and ensures smooth playback

### Character System
- **Animated AI companion** with idle behaviors, movement, and interaction states
- **Lip synchronization** using uLipSync for realistic mouth movement during speech
- **Dynamic character behavior** - Lulu moves around the room when not in conversation
- **Interaction-based conversations** - approach and press E to start talking

### Player Controller
- **First-person perspective** with smooth movement and camera controls
- **Unity Input System integration** supporting both keyboard/mouse and gamepad
- **Mobile-optimized controls** with touch input support
- **Interaction system** for engaging with objects and NPCs in the environment

### Backend Integration
- **n8n workflow automation** for conversation logging and user management
- **User identification system** with persistent UUID generation
- **Parent email registration** for account setup and safety monitoring
- **Content moderation pipeline** through automated workflows

### Technical Architecture
- **Unity 2022+ LTS** with Universal Render Pipeline (URP)
- **WebSocket communication** for real-time audio streaming
- **Modular component system** following Unity best practices
- **Thread-safe audio processing** with ring buffer implementation

## Game Flow

1. **Parent Setup**: Parents provide email address for account registration
2. **Room Exploration**: Children can freely explore the interactive playroom
3. **Character Interaction**: Approach Lulu and press interaction key to start conversation
4. **Voice Chat**: Engage in natural conversation with AI responses
5. **Dynamic Environment**: Lulu continues ambient behaviors when not in conversation

## Technical Highlights

- **Audio Processing**: Custom audio ring buffer system prevents voice dropouts
- **Animation System**: State-based character animations with smooth transitions  
- **Network Architecture**: Direct ElevenLabs integration with n8n webhook endpoints
- **Cross-Platform**: Designed for mobile deployment with desktop testing support
- **Safety-First**: Built-in content moderation and parental oversight systems

## Development Context

Created for a hackathon demonstrating AI voice interaction in Unity. The project showcases integration between Unity game development, conversational AI services, and automated workflow systems for creating safe, engaging experiences for children.

## Technologies Used

- **Unity 2022+ LTS** - Game engine and development platform
- **ElevenLabs Conversational AI** - Real-time voice chat and AI responses  
- **n8n** - Backend workflow automation and data processing
- **uLipSync** - Real-time lip synchronization for character animation
- **Unity Input System** - Modern input handling for multiple platforms
- **C# WebSocket** - Real-time communication with audio streaming services
