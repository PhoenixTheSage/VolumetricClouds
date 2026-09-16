# Graph Report - VolumetricClouds  (2026-09-16)

## Corpus Check
- 46 files · ~15,625 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 515 nodes · 815 edges · 26 communities (24 shown, 2 thin omitted)
- Extraction: 97% EXTRACTED · 3% INFERRED · 0% AMBIGUOUS · INFERRED: 24 edges (avg confidence: 0.87)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `31adac18`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- CloudTextures
- Config
- AnomalyBridge
- SettingsScreen
- Agent Instructions
- TranspilerHelpers
- H L S L
- SettingsGenerator
- Config
- Preloader Helpers
- Element
- KeybindAttribute
- Config Dialog Example Screenshot
- Hashing
- Slider
- Dropdown
- Control
- Client Plugin
- Button
- CheckboxAttribute
- Element 2
- Separator
- Textbox
- ClientPlugin.Settings.Elements
- Attribute
- clean.sh

## God Nodes (most connected - your core abstractions)
1. `CloudTextures` - 20 edges
2. `Config` - 20 edges
3. `SettingsGenerator` - 17 edges
4. `TranspilerHelpers` - 17 edges
5. `CloudTexture2D` - 16 edges
6. `CloudTexture3D` - 16 edges
7. `PreloaderHelpers` - 16 edges
8. `AnomalyBridge` - 15 edges
9. `ClientPlugin.Settings.Elements` - 15 edges
10. `Control` - 15 edges

## Surprising Connections (you probably didn't know these)
- `GitHub Copilot Instructions` --semantically_similar_to--> `VS Code Agents Instructions`  [INFERRED] [semantically similar]
  .github/copilot-instructions.md → .vscode/AGENTS.md
- `HLSL Skill README (.agents)` --semantically_similar_to--> `HLSL Skill README (.cursor)`  [INFERRED] [semantically similar]
  .agents/skills/a5c-ai-babysitter-hlsl/README.md → .cursor/skills/a5c-ai-babysitter-hlsl/README.md
- `HLSL Skill (.agents)` --semantically_similar_to--> `HLSL Skill (.cursor)`  [INFERRED] [semantically similar]
  .agents/skills/a5c-ai-babysitter-hlsl/SKILL.md → .cursor/skills/a5c-ai-babysitter-hlsl/SKILL.md
- `IsolatedMix Energy` --conceptually_related_to--> `AfterAtmosphere IsolatedMix volumetric.clouds`  [INFERRED]
  AGENTS.md → README.md
- `March LOD` --conceptually_related_to--> `Raymarch Quality Presets`  [INFERRED]
  AGENTS.md → README.md

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Agent Instruction Bootstrap** — github_copilot_instructions, vscode_agents, agents [EXTRACTED 1.00]
- **Color Row Family** — docs_configdialogexample_color_picker, docs_configdialogexample_color_with_alpha, docs_configdialogexample_hex_color_field [EXTRACTED 1.00]
- **Config Demo Widget Set** — docs_configdialogexample_toggle, docs_configdialogexample_integer_slider, docs_configdialogexample_number_slider, docs_configdialogexample_text_field, docs_configdialogexample_dropdown, docs_configdialogexample_color_picker, docs_configdialogexample_color_with_alpha, docs_configdialogexample_keybind, docs_configdialogexample_action_button [EXTRACTED 1.00]
- **Volumetric Cloud Pipeline** — readme_isolatedmix_volumetric_clouds, readme_cloudlayers, readme_mycloudrenderer_render, readme_quality_presets, readme_safetyscale, readme_hdr_lift [EXTRACTED 1.00]
- **Numeric Slider Family** — docs_configdialogexample_integer_slider, docs_configdialogexample_number_slider, docs_configdialogexample_numeric_type_split [INFERRED 0.85]
- **Mirrored HLSL Skill Copies** — agents_skills_a5c_ai_babysitter_hlsl_readme, agents_skills_a5c_ai_babysitter_hlsl_skill, cursor_skills_a5c_ai_babysitter_hlsl_readme, cursor_skills_a5c_ai_babysitter_hlsl_skill [INFERRED 0.95]

## Communities (26 total, 2 thin omitted)

### Community 0 - "CloudTextures"
Cohesion: 0.06
Nodes (31): CloudTexture2D, Name, Resource, Size, Size3, Srv, CloudTexture3D, Name (+23 more)

### Community 1 - "Config"
Cohesion: 0.10
Nodes (18): Layout, SettingsPanelSize, Func, List, MyGuiControlBase, Vector2, None, SettingsPanelSize (+10 more)

### Community 2 - "AnomalyBridge"
Cohesion: 0.08
Nodes (20): Assembly, AnomalyBridge, HasDisplayTenant, IsRegistered, PackRoot, IEnumerable, ISrvBindable, MethodInfo (+12 more)

### Community 3 - "SettingsScreen"
Cohesion: 0.05
Nodes (25): CloudSampler, Config, Vector3, Plugin, Instance, MethodImpl, AnomalyTerminalHook, Action (+17 more)

### Community 4 - "Agent Instructions"
Cohesion: 0.11
Nodes (31): Agent Instructions, Anomaly Owns Shared Rendering Gaps, CometWorks Skills, Docs/Extensibility.md Slice AI, IsolatedMix Energy, March LOD, Rich HUD Deferred Config Save, se-dev Skill (+23 more)

### Community 5 - "TranspilerHelpers"
Cohesion: 0.17
Nodes (13): CodeInstructionNotFound, TranspilerHelpers, CodeInstruction, CodeInstructionPredicate, IEnumerable, List, MethodBase, MethodInfo (+5 more)

### Community 6 - "H L S L"
Cohesion: 0.08
Nodes (28): HLSL Skill README (.agents), GPU Compute, DirectX Shaders, GLSL Skill, HLSL, Shader Optimization Skill, Unreal/Unity Shader Authoring, HLSL Skill (.agents) (+20 more)

### Community 7 - "SettingsGenerator"
Cohesion: 0.16
Nodes (11): AttributeInfo, SettingsGenerator, ActiveLayout, Dialog, Action, Func, List, MethodInfo (+3 more)

### Community 8 - "Config"
Cohesion: 0.08
Nodes (23): CloudQuality, High, Low, Medium, Ultra, Config, AlbedoTint, CirrusStrength (+15 more)

### Community 9 - "Preloader Helpers"
Cohesion: 0.21
Nodes (9): PreloaderHelpers, CodeInstructionPredicate, Instruction, List, Collection, FieldReference, MethodDefinition, MethodReference (+1 more)

### Community 10 - "Element"
Cohesion: 0.20
Nodes (22): Element, Path, _detect_pulsar_dir(), _detect_space_engineers(), _generate_guid(), _get_install_locations(), _get_linux_steam_path(), _get_steam_path() (+14 more)

### Community 11 - "KeybindAttribute"
Cohesion: 0.07
Nodes (25): ColorAttribute, SupportedTypes, Action, Color, Func, List, Type, ControlButtonData (+17 more)

### Community 12 - "Config Dialog Example Screenshot"
Cohesion: 0.12
Nodes (21): Config Dialog Example Screenshot, Action Button, Dialog Close Button, Color Picker RGB, Color Picker RGBA, Config Demo Dialog, Dropdown Combo Box, Hex Color Text Field (+13 more)

### Community 13 - "Hashing"
Cohesion: 0.24
Nodes (7): Hashing, CodeInstruction, IEnumerable, Instruction, MethodImpl, MethodInfo, ConstructorInfo

### Community 14 - "Slider"
Cohesion: 0.18
Nodes (10): SliderAttribute, SupportedTypes, SliderType, Float, Integer, Action, Func, List (+2 more)

### Community 15 - "Dropdown"
Cohesion: 0.28
Nodes (6): DropdownAttribute, SupportedTypes, Action, Func, List, Type

### Community 16 - "Control"
Cohesion: 0.40
Nodes (4): Control, MyGuiControlBase, Vector2, MyGuiDrawAlignEnum

### Community 17 - "Client Plugin"
Cohesion: 0.25
Nodes (6): ClientPlugin, net10.0, net48, Krafs.Publicizer (2.3.0), Lib.Harmony (2.4.2), Mono.Cecil (0.11.6)

### Community 18 - "Button"
Cohesion: 0.29
Nodes (6): ButtonAttribute, SupportedTypes, Action, Func, List, Type

### Community 19 - "CheckboxAttribute"
Cohesion: 0.29
Nodes (6): CheckboxAttribute, SupportedTypes, Action, Func, List, Type

### Community 21 - "Element 2"
Cohesion: 0.29
Nodes (6): IElement, SupportedTypes, Action, Func, List, Type

### Community 22 - "Separator"
Cohesion: 0.29
Nodes (6): SeparatorAttribute, SupportedTypes, Action, Func, List, Type

### Community 23 - "Textbox"
Cohesion: 0.29
Nodes (6): TextboxAttribute, SupportedTypes, Action, Func, List, Type

### Community 25 - "Attribute"
Cohesion: 0.25
Nodes (5): Attribute, IgnoresAccessChecksToAttribute, AssemblyName, System.Runtime.CompilerServices, ClientPlugin.Tools

## Knowledge Gaps
- **89 isolated node(s):** `IsRegistered`, `PackRoot`, `HasDisplayTenant`, `net10.0`, `net48` (+84 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 197 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **2 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `System.Runtime.CompilerServices` connect `Attribute` to `Config`, `TranspilerHelpers`, `Hashing`?**
  _High betweenness centrality (0.184) - this node is a cross-community bridge._
- **Why does `SettingsGenerator` connect `SettingsGenerator` to `ClientPlugin.Settings.Elements`, `Control`, `SettingsScreen`, `Config`?**
  _High betweenness centrality (0.148) - this node is a cross-community bridge._
- **Why does `ClientPlugin.Settings.Elements` connect `ClientPlugin.Settings.Elements` to `Config`, `KeybindAttribute`, `Slider`, `Dropdown`, `Control`, `Button`, `CheckboxAttribute`, `Element 2`, `Separator`, `Textbox`?**
  _High betweenness centrality (0.143) - this node is a cross-community bridge._
- **What connects `IsRegistered`, `PackRoot`, `HasDisplayTenant` to the rest of the system?**
  _89 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `CloudTextures` be split into smaller, more focused modules?**
  _Cohesion score 0.05587808417997097 - nodes in this community are weakly interconnected._
- **Should `Config` be split into smaller, more focused modules?**
  _Cohesion score 0.10144927536231885 - nodes in this community are weakly interconnected._
- **Should `AnomalyBridge` be split into smaller, more focused modules?**
  _Cohesion score 0.07807807807807808 - nodes in this community are weakly interconnected._