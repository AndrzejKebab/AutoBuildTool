# Auto Build System (ABS) for Unity

**AutoBuildTool** is a editor utility for Unity that automates the process of creating client and server builds. It handles semantic version bumping, supports Unity's `BuildProfile` system, and allows you to define custom folder hierarchies and files to be automatically generated alongside your builds.

---

## 📂 Project Structure

```
ABS/Editor/
├── Build/
│   ├── AutoBuildScript.cs      # Main build pipeline
│   ├── AutoBuildSettings.cs    # ScriptableObject config
│   ├── CustomFolder.cs         # Folder definition
│   └── CustomFile.cs           # File definition
│
├── AutoBuildSettingsEditor.cs  # Custom inspector UI
├── BuildFolderTreeView.cs      # Tree view logic
└── BuildFolderTreeItem.cs      # Tree item model
```

---

## ✨ Features

- **Automated Client & Server Builds**: Build both your client and server sequentially with a single click.
- **Build Profiles Support**: Easily assign multiple Unity `BuildProfiles` for varying target platforms or configurations.
- **Custom Post-Build Directories**: Visually design a custom folder hierarchy to be generated inside the build folder.
- **Custom Post-Build Files**: Automatically create text files (e.g., `README.txt`, configuration `.json`, or `.ini` files) with predefined content using the built-in text editor.
- **Semantic Versioning**: Automatically bump your project's version (Major, Minor, Patch, or Build) directly from the toolbar. Format: `Major.Minor.Patch:Build`.
- **Advanced TreeView GUI**: A robust, user-friendly inspector featuring right-click context menus, nested folders, and full keyboard shortcut support (Copy, Paste, Duplicate, Delete, Rename).

---

## 🚀 Getting Started

### Prerequisites
- Unity version supporting `BuildProfile` and `[SerializeReference]` (Unity 6.3+ recommended).
- Ensure your project has standard Build Profiles set up (`File > Build Profiles`).

### Installation
1. Place the `AutoBuildTool` scripts into an `Assets` folder.
2. Once Unity compiles, a new **Build** menu will appear in the top menu bar.

---

## 📖 Usage

### 1. Accessing Settings
Navigate to **`Build > Auto Build Settings`** in the top menu.
*If a settings asset doesn't exist, the tool will automatically create one at `Assets/Editor/AutoBuildSettings.asset` and select it.*

### 2. Configure Builds

#### Client

* Assign **Build Profiles**
* Add **Custom Folders / Files**

#### Server (optional)

* Enable `Enable Server Build`
* Assign server profiles
* Configure additional content

### 3. Adding Custom Folders & Files
Use the **Folder Tree** section to construct the directories and files that should be packaged with your game.

- **Add Root Folder/File**: Click the buttons below the tree view, or right-click in the empty space and select Add.
- **Edit File Content**: Click on any generated file in the TreeView. A text editor will appear at the bottom of the inspector, allowing you to write the contents of the file.
- **Nesting**: Right-click on any folder in the TreeView to add sub-folders or files inside it.

#### Actions

* Add root folders/files
* Rename (double-click or `R`)
* Context menu (right-click)

#### Keyboard Shortcuts

| Shortcut           | Action     |
|--------------------| ---------- |
| `Ctrl + N`         | New File   |
| `Ctrl + Shift + N` | New Folder |
| `R`                | Rename     |
| `Delete`           | Delete     |
| `Ctrl + C`         | Copy       |
| `Ctrl + V`         | Paste      |
| `Ctrl + D`         | Duplicate  |

---

## 🔢 Versioning System
Example:

```
1.2.0:15
```

### Bump Types

| Type  | Result              |
| ----- | ------------------- |
| Build | 1.2.0:15 → 1.2.0:16 |
| Patch | 1.2.0 → 1.2.1:0     |
| Minor | 1.2.0 → 1.3.0:0     |
| Major | 1.2.0 → 2.0.0:0     |

---

## ⚙️ How It Works

1. Build is triggered via menu
2. Version is bumped
3. Unity builds using selected **Build Profiles**
4. Output folder is created
5. Custom folders/files are injected

---

## 🧠 Notes

* Only **one `AutoBuildSettings` asset** should exist
* If multiple are found, the first one is used (warning logged)
* Server build is optional and fully toggleable
* Uses `BuildOptions.CompressWithLz4HC` for optimized builds

---

**Output Structure:**
Builds are output to a `Builds/` folder in your project root, categorized by the safe version name:
```text
MyProject/
  ├─ Builds/
  │  ├─ v.1.0.0_1/
  │  │  ├─ Client/
  │  │  │  ├─ MyGame.exe
  │  │  │  ├─ (Your Custom Folders & Files)
  │  │  ├─ Server/
  │  │  │  ├─ MyGame_Server.exe
  │  │  │  ├─ (Your Custom Folders & Files)
```

---

## 🛠️ Technical Notes
* **Executable Names**: The client .exe defaults to PlayerSettings.productName, while the server appends _Server to the product name.

---

## 📌 Future Improvements (Ideas)

* Drag & drop support in tree view
* Custom File content generation (e.g. JSON config with list like editor)

| Type    | Name       | Value          |
|---------|------------|----------------|
| String  | playerName | "AndrzejKebab" |
| Number  | playerHP   | 100            |
| Bool    | IsDead     | False          |
| Nested? | ---------- | -------------- |

---