using System;
using System.Collections.Generic;
using UnityEngine;

namespace ABS.Build
{
	[Serializable]
	public class CustomFolder
	{
		public                      string             Name;
		public                      List<CustomFile>   Files      = new();
		[SerializeReference] public List<CustomFolder> SubFolders = new();
	}
}