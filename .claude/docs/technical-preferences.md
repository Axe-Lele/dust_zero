# Clean Girls technical preferences

Project-specific fallback; project.yaml and CLAUDE.md take precedence.
- **Engine**: Unity 6000.4.5f1
- **Language**: C#
- **Rendering**: URP 2D
- **UI**: uGUI + TextMeshPro with Korean fonts
- **Physics**: Explicit original distance/grid rules; do not substitute physics or NavMesh for combat rules
- **Target Platforms**: Portrait mobile, Windows editor for development
- **Input Methods**: Touch and keyboard/mouse
- **Primary Input**: Touch
- **Touch Support**: Full
- **Gamepad Support**: None required
- **Classes**: PascalCase; preserve existing namespaces and style
- **Framework**: unity-test-framework
- **Primary**: unity-specialist
- **Language/Code Specialist**: unity-specialist
- **Shader Specialist**: unity-shader-specialist
- **UI Specialist**: unity-ui-specialist (uGUI requirement overrides generic UI Toolkit advice)
- **Allowed Libraries**: Existing URP, Input System, Newtonsoft JSON, uGUI/TMP, DOTween, Unity pooling/test framework
- **Forbidden Patterns**: WebView wrapper, monolithic game script, duplicated balance values, unsolicited ECS/DOTS/DI adoption
- **Project root**: DustZeroUnity; game sources: DustZeroUnity/Assets/DustZero/Scripts
