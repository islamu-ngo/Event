using YamlDotNet.RepresentationModel;

var stream = new YamlStream();
stream.Load(new StringReader("""
    root:
      value: stable
    """));

if (stream.Documents.Count != 1
    || stream.Documents[0].RootNode is not YamlMappingNode root
    || !root.Children.ContainsKey(new YamlScalarNode("root")))
    return 1;

return 0;
