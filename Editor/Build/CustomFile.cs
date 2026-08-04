using System;
using UnityEngine;

namespace AutoBuildTool.Editor.Build
{
	[Serializable]
	public struct CustomFile
	{
		public                   string Name;
		[TextArea(1, 20)] public string FileContent;
	}
}