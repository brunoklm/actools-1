﻿using System;
using System.IO;
using AcTools.Utils;
using JetBrains.Annotations;

namespace AcTools.Kn5File {
    public interface IKn5TextureProvider {
        void GetTexture([NotNull] string textureName, Func<long, Stream> writer);
    }

    public partial class Kn5 {
        private void SaveInner(string filename) {
            using (var writer = new Kn5Writer(File.Open(filename, FileMode.Create, FileAccess.ReadWrite))) {
                writer.Write(Header);

                writer.Write(Textures.Count);
                foreach (var texture in Textures.Values) {
                    var data = TexturesData[texture.Name];
                    texture.Length = data.Length;
                    writer.Write(texture);
                    writer.Write(data);
                }

                writer.Write(Materials.Count);
                foreach (var material in Materials.Values) {
                    writer.Write(material);
                }

                Save_Node(writer, RootNode);
            }
        }

        private static void Save_Node(Kn5Writer writer, Kn5Node node) {
            writer.Write(node);
            foreach (var t in node.Children) {
                Save_Node(writer, t);
            }
        }

        public void SaveNew(string filename) {
            SaveInner(filename);
        }

        public void SaveRecyclingOriginal(string filename) {
            using (var f = FileUtils.RecycleOriginal(filename)) {
                SaveInner(f.Filename);
            }
        }
    }
}
