using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AutoBuildTool.Editor.Build
{
	public enum FileOperationType
	{
		CreateTextFile,
		CopyProjectAsset
	}

	[Serializable]
	public struct CustomFile
	{
		public                   FileOperationType OperationType;
		public                   string            Name;
		[TextArea(1, 20)] public string            FileContent;
		public                   Object            SourceAsset;
	}
}