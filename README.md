# Lulu - AI Voice Companion for Children

A Unity-based interactive game where children (ages 6-10) can have real-time voice conversations with Lulu, an AI companion character powered by ElevenLabs Conversational AI.

## Project Overview

Lulu creates an immersive playroom environment where children can explore, interact with objects, and engage in natural voice conversations with an AI character. The project demonstrates seamless integration between Unity game development, conversational AI services, and automated workflow systems designed specifically for child safety and engagement.

## Core Features

### 🎮 Interactive Game Environment
- **First-person exploration** of a colorful playroom with interactive objects
- **Mobile and desktop support** with touch-optimized controls
- **Unity Input System integration** supporting keyboard, mouse, and gamepad
- **Object interaction system** for engaging with toys and environment elements

### 🤖 AI-Powered Character System
- **Lulu - Interactive AI Companion** with personality designed for children
- **Real-time voice conversations** using ElevenLabs Conversational AI
- **Dynamic character behaviors** - Lulu moves around and performs ambient animations when not in conversation
- **Lip synchronization** using uLipSync for realistic mouth movement during speech
- **Interaction-based conversations** - approach Lulu and press E to start talking

### 🎙️ Advanced Voice Technology
- **Bidirectional audio streaming** with WebSocket real-time communication
- **Custom audio buffering system** prevents voice dropouts and ensures smooth playback
- **16kHz audio processing** optimized for conversational AI quality
- **Thread-safe audio pipeline** with ring buffer implementation
- **Cross-platform microphone support** with automatic device selection

### 🔧 Technical Architecture
- **Unity 2022+ LTS** with Universal Render Pipeline (URP)
- **Modular component system** following Unity best practices and separation of concerns
- **Event-driven architecture** for clean component communication
- **WebSocket integration** for real-time audio streaming to ElevenLabs
- **Persistent user identification** with UUID-based user management

### 🛡️ Safety and Monitoring
- **Parent email registration** for account setup and oversight
- **n8n workflow integration** for conversation logging and content moderation
- **User session management** with automatic conversation timeouts
- **Content filtering pipeline** through automated backend workflows

## Game Flow

1. **Parent Setup**: Parents provide email address during initial game launch
2. **Room Exploration**: Children freely explore the interactive playroom environment
3. **Character Discovery**: Find and approach Lulu to see interaction prompts
4. **Voice Conversations**: Press interaction key to start natural voice chat with AI responses
5. **Ambient Experience**: Lulu continues exploring and animating when conversations end

## Technical Implementation

### Voice Chat System
The project implements a sophisticated real-time voice chat system:

```csharp
// Core voice chat component with WebSocket streaming
ElevenLabsVoiceChat voiceChat = GetComponent<ElevenLabsVoiceChat>();
voiceChat.StartConversation(); // Opens WebSocket to ElevenLabs agent
```

**Audio Processing Pipeline:**
- Microphone capture → 16kHz resampling → Base64 encoding → WebSocket transmission
- ElevenLabs response → PCM decoding → Audio ring buffer → Unity AudioSource playback

### Character Animation System
Lulu features a comprehensive animation system with multiple behavioral states:

```csharp
// Animation manager handling ambient behaviors and conversation states  
LuluAnimationManager animManager = GetComponent<LuluAnimationManager>();
animManager.StartConversation(); // Switches to conversation-focused behavior
```

**Animation States:**
- **Idle Variations**: Random actions like picking up items, looking around, turning
- **Movement**: Walking between waypoints with pathfinding
- **Conversation**: Faces player and maintains attention during chat
- **Transitions**: Smooth state changes between behavioral modes

### Interaction System
Built on Unity's component-based architecture with clear interfaces:

```csharp
// Interaction interface implementation for consistent object behavior
public class LuluInteractable : MonoBehaviour, IInteractable
{
    public void OnInteract(GameObject player) 
    { 
        // Start or end conversation based on current state
    }
}
```

## Backend Integration

### n8n Workflow Automation
Three core workflows handle data processing and safety:

1. **Conversation Storage**: Logs all chat sessions with timestamps and user context
2. **User Context Management**: Handles user identification and conversation history retrieval  
3. **Content Moderation**: Real-time safety monitoring and inappropriate content flagging

### Data Flow Architecture
```
Unity Game ↔ ElevenLabs Conversational AI ↔ n8n Workflows
     ↓                    ↓                        ↓
Player Input → AI Processing → Backend Automation
```

This architecture provides:
- Real-time conversational experience
- Automated safety monitoring  
- Persistent user data management
- Scalable content moderation

## Development Highlights

### Performance Optimizations
- **Audio Ring Buffer**: Prevents dropouts during network variations
- **Threaded WebSocket**: Non-blocking real-time communication
- **Component Caching**: Reduces runtime lookups and garbage collection
- **Burst Compilation**: High-performance audio processing with Unity Jobs System

### Child-Friendly Design Patterns
- **Visual Interaction Cues**: Clear indicators for interactive elements
- **Simplified Controls**: Age-appropriate input methods
- **Consistent Feedback**: Immediate visual and audio responses to actions
- **Safe Exploration**: Designed environment with no punitive mechanics

### Cross-Platform Considerations  
- **Responsive Touch Controls**: Optimized for mobile devices
- **Scalable UI Elements**: Adapts to different screen sizes and resolutions
- **Platform-Specific Audio**: Handles various microphone implementations
- **Network Resilience**: Graceful handling of connection issues

## Technologies and Tools

**Game Engine and Frameworks:**
- Unity 2022+ LTS with Universal Render Pipeline
- Unity Input System for modern input handling
- Unity Audio System with custom streaming extensions

**AI and Voice Processing:**
- ElevenLabs Conversational AI for natural language processing
- uLipSync for real-time facial animation
- Custom WebSocket implementation for audio streaming

**Backend and Automation:**
- n8n for workflow automation and data processing
- RESTful webhooks for Unity-to-backend communication
- JSON-based data serialization and API integration

**Development and Deployment:**
- C# 9+ with modern language features
- Component-based architecture following SOLID principles
- Git version control with modular development approach

## Project Context

This project was developed for a hackathon demonstrating the integration of Unity game development with modern AI conversational services. The implementation showcases how to create safe, engaging voice-interactive experiences for children while meeting technical requirements for automated workflow integration.

The project emphasizes:
- **Technical Excellence**: Production-quality code architecture and performance optimization
- **User Experience**: Child-centered design with intuitive interaction patterns  
- **Safety First**: Comprehensive monitoring and content moderation systems
- **Scalable Design**: Modular components that can be extended for additional features

## Architecture Philosophy

The codebase follows Unity best practices with emphasis on:
- **Separation of Concerns**: Clear boundaries between audio, networking, animation, and game logic
- **Event-Driven Design**: Loose coupling between systems through Unity Events and C# events
- **Defensive Programming**: Comprehensive error handling and graceful degradation
- **Performance Awareness**: Memory-efficient implementations with minimal garbage collection

This approach ensures the project can serve as both a compelling demo and a foundation for production development.
