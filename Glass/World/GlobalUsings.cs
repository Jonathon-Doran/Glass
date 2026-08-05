// Repo-wide alias names for the typed identifiers defined in Id.cs. Each alias binds a
// familiar name to one closed instantiation of Id<TTag>.
//
// Migration in progress: an alias must stay commented out until the legacy struct of the
// same name is deleted, or the two definitions collide. Enable one at a time.

global using MobId = Glass.World.Id<Glass.World.MobTag>;
global using ZoneId = Glass.World.Id<Glass.World.ZoneTag>;
global using SpawnId = Glass.World.Id<Glass.World.SpawnTag>;

global using OpcodeHandle = Glass.World.Id<Glass.World.OpcodeTag>;
global using CollectionHandle = Glass.World.Id<Glass.World.CollectionTag>;
global using CollectionIndex = Glass.World.Id<Glass.World.CollectionIndexTag>;
global using GateHandle = Glass.World.Id<Glass.World.GateTag>;
global using GateDefinitionHandle = Glass.World.Id<Glass.World.GateDefinitionTag>;
global using BagHandle = Glass.World.Id<Glass.World.BagTag>;
global using MessageIndex = Glass.World.Id<Glass.World.MessageIndexTag>;