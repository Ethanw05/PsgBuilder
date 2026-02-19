bl_info = {
    "name": "Blenrose Material Editor",
    "author": "Ethan + The Architect",
    "version": (0, 1, 0),
    "blender": (3, 0, 0),
    "location": "Material Properties > Blenrose Editor, or Search > Blenrose Material Editor",
    "description": "Melrose-like material editor for Skate 3 materials",
    "category": "Material",
}

import bpy
import os
import json
from pathlib import Path
import xml.etree.ElementTree as ET
from bpy.types import (
    Operator,
    Panel,
    PropertyGroup,
    UIList,
)


# Track temporary suppression of reactive node updates while we batch-assign
# detected texture/UV properties from an existing shader tree.
_SUPPRESSED_UPDATE_MATERIALS = set()
from bpy.props import (
    BoolProperty,
    EnumProperty,
    FloatProperty,
    PointerProperty,
    StringProperty,
    IntProperty,
)


# -------------------------------------------------------------------------
# Shared enums / helpers
# -------------------------------------------------------------------------

def uv_channel_items(self, context):
    """
    Build UV set enum dynamically from all unique UV map names in the scene.
    Enum identifier and label are both the actual UV map name so we can plug
    them directly into UV Map nodes.
    """
    uv_names = set()
    try:
        for obj in bpy.data.objects:
            if obj.type == "MESH" and getattr(obj.data, "uv_layers", None):
                for layer in obj.data.uv_layers:
                    if layer.name:
                        uv_names.add(layer.name)
    except Exception:
        uv_names = set()

    if not uv_names:
        return [
            ("UV0", "UV0", "UV0"),
            ("UV1", "UV1", "UV1"),
        ]

    return [(name, name, "") for name in sorted(uv_names)]


# Collision surface enums from Collision_Export_Dumbad_Tuukkas.py
AUDIO_SURFACE_ITEMS = [
    ('0', 'Undefined', 'Generic surface'),
    ('1', 'Asphalt_Smooth', 'Smooth asphalt'),
    ('2', 'Asphalt_Rough', 'Rough asphalt'),
    ('3', 'Concrete_Polished', 'Polished concrete'),
    ('4', 'Concrete_Rough', 'Rough concrete'),
    ('5', 'Concrete_Aggregate', 'Aggregate concrete'),
    ('6', 'Wood_Ramp', 'Wood ramp'),
    ('7', 'Plywood', 'Plywood'),
    ('8', 'Dirt', 'Dirt'),
    ('9', 'Metal', 'Metal'),
    ('10', 'Grass', 'Grass (sets mIsAboveGrass flag)'),
    ('11', 'Metal_Solid_Round_1', 'Metal solid round 1'),
    ('12', 'Metal_Solid_Round_1_Up', 'Metal solid round 1 up'),
    ('13', 'Metal_Solid_Round_2', 'Metal solid round 2'),
    ('14', 'Metal_Solid_Square_1', 'Metal solid square 1'),
    ('15', 'Metal_Solid_Square_2', 'Metal solid square 2'),
    ('16', 'Metal_Hollow_Round_1', 'Metal hollow round 1'),
    ('17', 'Metal_Hollow_Round_1_Dead', 'Metal hollow round 1 dead'),
    ('18', 'Metal_Hollow_Round_1_Dn', 'Metal hollow round 1 down'),
    ('19', 'Metal_Hollow_Round_2', 'Metal hollow round 2'),
    ('20', 'Metal_Hollow_Round_2_Dead', 'Metal hollow round 2 dead'),
    ('21', 'Metal_Hollow_Round_2_Dn', 'Metal hollow round 2 down'),
    ('22', 'Metal_Hollow_Round_3', 'Metal hollow round 3'),
    ('23', 'Metal_Hollow_Round_4', 'Metal hollow round 4'),
    ('24', 'Metal_Hollow_Square_1', 'Metal hollow square 1'),
    ('25', 'Metal_Hollow_Square_2', 'Metal hollow square 2'),
    ('26', 'Metal_Hollow_Square_3', 'Metal hollow square 3'),
    ('27', 'Metal_Hollow_Square_3_Dead', 'Metal hollow square 3 dead'),
    ('28', 'Metal_Hollow_Square_4', 'Metal hollow square 4'),
    ('29', 'Metal_Hollow_1', 'Metal hollow 1'),
    ('30', 'Metal_Hollow_2', 'Metal hollow 2'),
    ('31', 'Metal_Sheet', 'Metal sheet'),
    ('32', 'Metal_Complex_1', 'Metal complex 1'),
    ('33', 'Metal_Complex_2', 'Metal complex 2'),
    ('34', 'Metal_Complex_3', 'Metal complex 3'),
    ('35', 'Metal_Complex_4', 'Metal complex 4'),
    ('36', 'Metal_Complex_5', 'Metal complex 5'),
    ('37', 'Metal_Complex_6', 'Metal complex 6'),
    ('38', 'Metal_Complex_7', 'Metal complex 7'),
    ('39', 'Metal_Complex_8', 'Metal complex 8'),
    ('40', 'Metal_Complex_Debris', 'Metal complex debris'),
    ('41', 'Wood_1', 'Wood 1'),
    ('42', 'Wood_1_Up', 'Wood 1 up'),
    ('43', 'Wood_2', 'Wood 2'),
    ('44', 'Wood_3', 'Wood 3'),
    ('45', 'Wood_3_Up', 'Wood 3 up'),
    ('46', 'Wood_4', 'Wood 4'),
    ('47', 'Plastic_1', 'Plastic 1'),
    ('48', 'Plastic_2', 'Plastic 2'),
    ('49', 'Plastic_3', 'Plastic 3'),
    ('50', 'Plastic_4', 'Plastic 4'),
    ('51', 'Glass_Thick_Large', 'Glass thick large'),
    ('52', 'Glass_Thin_Small', 'Glass thin small'),
    ('53', 'Concrete_Curb', 'Concrete curb'),
    ('54', 'Concrete_Bench', 'Concrete bench'),
    ('55', 'Leaves', 'Leaves'),
    ('56', 'Bush', 'Bush'),
    ('57', 'Pottery', 'Pottery'),
    ('58', 'Paper', 'Paper'),
    ('59', 'Cardboard', 'Cardboard'),
    ('60', 'Garbage_Bag', 'Garbage bag'),
    ('61', 'Garbage_Spill', 'Garbage spill'),
    ('62', 'Bottle', 'Bottle'),
    ('63', 'Tile_Ceramic', 'Tile ceramic'),
    ('64', 'Marble_or_Slate', 'Marble or slate'),
    ('65', 'Brick_Smooth', 'Brick smooth'),
    ('66', 'Brick_Coarse', 'Brick coarse'),
    ('67', 'Manhole_Metal', 'Manhole metal'),
    ('68', 'Metal_Grate_Sewer', 'Metal grate sewer'),
    ('69', 'Metal_Grate_Planter', 'Metal grate planter'),
    ('70', 'DeepSnow', 'Deep snow'),
    ('71', 'PackedSnow', 'Packed snow'),
    ('72', 'Ice', 'Ice'),
    ('73', 'Antennas', 'Antennas'),
    ('74', 'Chandelier', 'Chandelier'),
    ('75', 'Plexiglass_Small', 'Plexiglass small'),
    ('76', 'Plexiglass_Large', 'Plexiglass large'),
    ('77', 'Potted_Plant', 'Potted plant'),
    ('78', 'Crumpled_Paper', 'Crumpled paper'),
    ('79', 'Cloth', 'Cloth'),
    ('80', 'Pop_Can', 'Pop can'),
    ('81', 'Paper_Cup', 'Paper cup'),
    ('82', 'Wire_Cable', 'Wire cable'),
    ('83', 'VolleyBall', 'Volleyball'),
    ('84', 'OilDrum', 'Oil drum'),
    ('85', 'DMORail', 'DMO rail'),
    ('86', 'Fruit', 'Fruit'),
    ('87', 'Plastic_Bottle', 'Plastic bottle'),
    ('88', 'Drum_Pylon', 'Drum pylon'),
    ('89', 'Metal_Rail_4', 'Metal rail 4'),
    ('90', 'Wood_5', 'Wood 5'),
    ('91', 'Metal_Ramp', 'Metal ramp'),
    ('92', 'Complex_Plastic_1', 'Complex plastic 1'),
    ('93', 'Max_Mappable_Surface', 'Max mappable surface'),
]

PHYSICS_SURFACE_ITEMS = [
    ('0', 'Undefined', 'Default physics'),
    ('1', 'Smooth', 'Fast/smooth movement'),
    ('2', 'Rough', 'Medium friction'),
    ('3', 'Slow', 'Slower movement'),
    ('4', 'Slippery', 'Low friction/slippery'),
    ('5', 'VerySlow', 'Very slow movement (BREADCRUMB BLOCKED)'),
    ('6', 'Unrideable', 'Cannot ride (BREADCRUMB BLOCKED)'),
    ('7', 'DoNotAlign', 'Special alignment behavior'),
    ('8', 'Stair', 'Stairs (BREADCRUMB BLOCKED, sets mIsAboveStairs)'),
    ('9', 'InstantBail', 'Immediately forces the player into a bail state'),
    ('10', 'SlipperyRagdoll', 'Ragdoll slides smoothly with no friction in bail state'),
    ('11', 'BouncyRagdoll', 'Ragdoll is bouncy in bail state'),
    ('12', 'Water', 'Causes the player to enter a swimming state'),
]

SURFACE_PATTERN_ITEMS = [
    ('0', 'None (Default)', 'No pattern'),
    ('1', 'SpiderCrack', 'Cracked surface'),
    ('2', 'Square2x2', '2x2 tile pattern'),
    ('3', 'Square4x4', '4x4 tile pattern'),
    ('4', 'Square8x8', '8x8 tile pattern'),
    ('5', 'Square12x12', '12x12 tile pattern'),
    ('6', 'Square24x24', '24x24 tile pattern'),
    ('7', 'IrregularSmall', 'Small irregular pattern'),
    ('8', 'IrregularMedium', 'Medium irregular pattern'),
    ('9', 'IrregularLarge', 'Large irregular pattern'),
    ('10', 'Slats', 'Slat pattern'),
    ('11', 'Sidewalk', 'Sidewalk pattern'),
    ('12', 'BrickTileRandomSize', 'Brick tile (random size)'),
    ('13', 'MiniTile', 'Mini tile pattern'),
    ('14', 'Special1', 'Special pattern 1'),
    ('15', 'Special2', 'Special pattern 2'),
]

# Material class / subclass tables.
# These are now fully hard-coded from
# documentation\\cPres Documentation\\Skate3MaterialInfo
MATERIAL_CLASS_ITEMS = [
    ("ADVERTISEMENT", "advertisement", ""),
    ("ANIMATED", "animated", ""),
    ("BASIC", "basic", ""),
    ("BUILDING", "building", ""),
    ("CHARACTER", "character", ""),
    ("CHARATTRIBUTES", "charattributes", ""),
    ("DEFAULTENVIRONMENT", "defaultenvironment", ""),
    ("DMOATTRIBUTES", "dmoattributes", ""),
    ("DYNAMICOBJECT", "dynamicobject", ""),
    ("ENVATTRIBUTES", "envattributes", ""),
    ("ENVIRONMENT", "environment", ""),
    ("ENVIRONMENTPARK", "environmentpark", ""),
    ("ENVIRONMENTSIMPLE", "environmentsimple", ""),
    ("FOG", "fog", ""),
    ("GLARE", "glare", ""),
    ("GODRAY", "godray", ""),
    ("INCANDESCENT", "incandescent", ""),
    ("NAMEMAP", "namemap", ""),
    ("OCEAN", "ocean", ""),
    ("PROXYWORLD", "proxyworld", ""),
    ("SKY", "sky", ""),
    ("TERRAIN", "terrain", ""),
    ("TRAFFICLIGHT", "trafficlight", ""),
    ("TREE", "tree", ""),
    ("VEHICLE", "vehicle", ""),
    ("VISUALINDICATOR", "visualindicator", ""),
    ("WATER", "water", ""),
]

MATERIAL_SUBCLASSES = {
    "ADVERTISEMENT": [
        ("DEFAULT", "default", ""),
    ],
    "ANIMATED": [
        ("DEFAULT", "default", ""),
        ("FLAG", "flag", ""),
        ("SHRUB", "shrub", ""),
        ("TREE", "tree", ""),
    ],
    "BASIC": [
        ("DEFAULT", "default", ""),
    ],
    "BUILDING": [
        ("CEILINGLOCAL", "ceilinglocal", ""),
        ("DEFAULT", "default", ""),
        ("INTERIORCUBE", "interiorcube", ""),
    ],
    "CHARACTER": [
        ("ALPHA", "alpha", ""),
        ("CAC", "cac", ""),
        ("CLOTH_ROPA", "cloth_ropa", ""),
        ("CLOTH_STAMP_ROPA", "cloth_stamp_ropa", ""),
        ("CLOTH_STAMP", "cloth_stamp", ""),
        ("CLOTH", "cloth", ""),
        ("DEFAULT_CLOTH_ROPA", "default_cloth_ropa", ""),
        ("DEFAULT_CLOTH", "default_cloth", ""),
        ("DEFAULT_HAIR_ROPA", "default_hair_ropa", ""),
        ("DEFAULT_HAIR", "default_hair", ""),
        ("DEFAULT_HOM", "default_hom", ""),
        ("DEFAULT_LIGHT", "default_light", ""),
        ("DEFAULT_SKATEBOARD", "default_skateboard", ""),
        ("DEFAULT_SKIN", "default_skin", ""),
        ("DEFAULT", "default", ""),
        ("FACE", "face", ""),
        ("HAIR_ROPA", "hair_ropa", ""),
        ("HAIR", "hair", ""),
        ("LEATHER", "leather", ""),
        ("LIVINGWORLD_DYNAMICOBJ", "livingworld_dynamicobj", ""),
        ("LIVINGWORLD_PEDESTRIANS_STAMP", "livingworld_pedestrians_stamp", ""),
        ("LIVINGWORLD_PEDESTRIANS", "livingworld_pedestrians", ""),
        ("LIVINGWORLD_VEHICLES_GLASS", "livingworld_vehicles_glass", ""),
        ("LIVINGWORLD_VEHICLES", "livingworld_vehicles", ""),
        ("LIVINGWORLD", "livingworld", ""),
        ("SHIFT", "shift", ""),
        ("SKIN", "skin", ""),
    ],
    "CHARATTRIBUTES": [
        ("DEFAULT", "default", ""),
    ],
    "DEFAULTENVIRONMENT": [
        ("DEFAULT", "default", ""),
        ("TRANSPARENT", "transparent", ""),
    ],
    "DMOATTRIBUTES": [
        ("DEFAULT", "default", ""),
    ],
    "DYNAMICOBJECT": [
        ("ALPHATEST", "alphatest", ""),
        ("DEFAULT", "default", ""),
    ],
    "ENVATTRIBUTES": [
        ("DEFAULT", "default", ""),
    ],
    "ENVIRONMENT": [
        ("DECAL_SIMPLE", "decal_simple", ""),
        ("DECAL_TILEABLE_SIMPLE", "decal_tileable_simple", ""),
        ("DECAL_TILEABLE", "decal_tileable", ""),
        ("DECAL", "decal", ""),
        ("DECAL2", "decal2", ""),
        ("DEFAULT", "default", ""),
        ("REFLECTIVE_SIMPLE", "reflective_simple", ""),
        ("REFLECTIVE_TRANS", "reflective_trans", ""),
        ("REFLECTIVE", "reflective", ""),
        ("TRANSPARENT", "transparent", ""),
    ],
    "ENVIRONMENTPARK": [
        ("ALPHATEST", "alphatest", ""),
        ("DECAL", "decal", ""),
        ("DIFFUSE", "diffuse", ""),
    ],
    "ENVIRONMENTSIMPLE": [
        ("ALPHATEST", "alphatest", ""),
        ("BACKGROUND", "background", ""),
        ("DEFAULT", "default", ""),
        ("DIFFUSE", "diffuse", ""),
    ],
    "FOG": [
        ("DEFAULT", "default", ""),
        ("FOG_BACKUP", "fog_backup", ""),
        ("FOG_BIGEVENT", "fog_bigevent", ""),
        ("FOG_CAS", "fog_cas", ""),
        ("FOG_COOL", "fog_cool", ""),
        ("FOG_DARK", "fog_dark", ""),
        ("FOG_DEF_FAR", "fog_def_far", ""),
        ("FOG_DEFAULT", "fog_default", ""),
        ("FOG_FEMAIN", "fog_femain", ""),
        ("FOG_FINANCIAL", "fog_financial", ""),
        ("FOG_HAZE", "fog_haze", ""),
        ("FOG_INSIDE", "fog_inside", ""),
        ("FOG_LIGHT", "fog_light", ""),
        ("FOG_OLDTOWN", "fog_oldtown", ""),
        ("FOG_PROJECTS", "fog_projects", ""),
        ("FOG_REZ", "fog_rez", ""),
        ("FOG_SLAPPY", "fog_slappy", ""),
        ("FOG_SLAPPYWAREHOUSE", "fog_slappywarehouse", ""),
        ("FOG_SOHO", "fog_soho", ""),
        ("FOG_TRICKGUIDE", "fog_trickguide", ""),
        ("FOG_WARM", "fog_warm", ""),
        ("FOG_WATERFRONT", "fog_waterfront", ""),
    ],
    "GLARE": [
        ("DEFAULT", "default", ""),
    ],
    "GODRAY": [
        ("DEFAULT", "default", ""),
        ("SPOTLIGHT", "spotlight", ""),
    ],
    "INCANDESCENT": [
        ("BACKLIT", "backlit", ""),
        ("BACKLITUVSCROLL", "backlituvscroll", ""),
        ("DEFAULT", "default", ""),
        ("TRANSPARENT", "transparent", ""),
        ("VIDEOSCREEN", "videoscreen", ""),
    ],
    "NAMEMAP": [
        ("DEFAULT", "default", ""),
    ],
    "OCEAN": [
        ("DEFAULT", "default", ""),
        ("REFLECTION", "reflection", ""),
    ],
    "PROXYWORLD": [
        ("DEFAULT", "default", ""),
    ],
    "SKY": [
        ("DEFAULT", "default", ""),
        ("SKY_70S", "sky_70s", ""),
        ("SKY_BKUP1", "sky_bkup1", ""),
        ("SKY_BKUP2", "sky_bkup2", ""),
        ("SKY_BKUP3", "sky_bkup3", ""),
        ("SKY_CREATURE", "sky_creature", ""),
        ("SKY_DANNY", "sky_danny", ""),
        ("SKY_DARK", "sky_dark", ""),
        ("SKY_DEFAULT", "sky_default", ""),
        ("SKY_FE2", "sky_fe2", ""),
        ("SKY_FEMAIN", "sky_femain", ""),
        ("SKY_FESHIP", "sky_feship", ""),
        ("SKY_FESTART", "sky_festart", ""),
        ("SKY_FINANCIAL", "sky_financial", ""),
        ("SKY_LIGHT", "sky_light", ""),
        ("SKY_OLDTOWN", "sky_oldtown", ""),
        ("SKY_PROJECTS", "sky_projects", ""),
        ("SKY_PURE", "sky_pure", ""),
        ("SKY_REZ", "sky_rez", ""),
        ("SKY_SLAPPY", "sky_slappy", ""),
        ("SKY_SOHO", "sky_soho", ""),
        ("SKY_SVM", "sky_svm", ""),
        ("SKY_WATERFRONT", "sky_waterfront", ""),
    ],
    "TERRAIN": [
        ("DECAL", "decal", ""),
        ("DEFAULT", "default", ""),
    ],
    "TRAFFICLIGHT": [
        ("DEFAULT", "default", ""),
        ("ONE", "one", ""),
        ("TWO", "two", ""),
    ],
    "TREE": [
        ("DEFAULT", "default", ""),
    ],
    "VEHICLE": [
        ("DEFAULT", "default", ""),
        ("LIGHT", "light", ""),
        ("LOD01", "lod01", ""),
    ],
    "VISUALINDICATOR": [
        ("DEFAULT", "default", ""),
    ],
    "WATER": [
        ("ALPHA", "alpha", ""),
        ("DEFAULT", "default", ""),
        ("FLOWING", "flowing", ""),
        ("FLOWINGALPHA", "flowingalpha", ""),
        ("SKATEPARK_ALPHA", "skatepark_alpha", ""),
        ("SKATEPARK", "skatepark", ""),
    ],
}

MATERIAL_SUBCLASS_FALLBACK = [("DEFAULT", "default", "")]


def _get_owner_material(settings):
    """Helper to get the owning material from a BlenroseMaterialSettings instance."""
    mat = getattr(settings, "id_data", None)
    if isinstance(mat, bpy.types.Material):
        return mat
    return None




def _guess_active_uv_for_material(mat):
    """Best-effort fallback UV map name for materials without explicit UV Map nodes."""
    if not mat:
        return None

    uv_name_counts = {}
    for obj in bpy.data.objects:
        if obj.type != "MESH" or not getattr(obj, "material_slots", None):
            continue
        if not any(slot.material == mat for slot in obj.material_slots):
            continue

        mesh = obj.data
        uv_layers = getattr(mesh, "uv_layers", None)
        if not uv_layers:
            continue

        preferred = None
        for layer in uv_layers:
            if getattr(layer, "active_render", False) and layer.name:
                preferred = layer.name
                break
        if not preferred and getattr(uv_layers, "active", None) and uv_layers.active.name:
            preferred = uv_layers.active.name
        if not preferred and len(uv_layers) > 0 and uv_layers[0].name:
            preferred = uv_layers[0].name

        if preferred:
            uv_name_counts[preferred] = uv_name_counts.get(preferred, 0) + 1

    if not uv_name_counts:
        return None

    return max(uv_name_counts.items(), key=lambda kv: kv[1])[0]


def _trace_uv_map_name_from_socket(nt, socket, mat, visited_nodes=None, depth=0):
    """Walk upstream from a vector socket and resolve an explicit or implied UV map name."""
    if depth > 8 or socket is None:
        return None
    if visited_nodes is None:
        visited_nodes = set()

    for link in nt.links:
        if link.to_socket != socket:
            continue

        from_node = link.from_node
        if not from_node:
            continue

        node_ptr = from_node.as_pointer()
        if node_ptr in visited_nodes:
            continue
        visited_nodes.add(node_ptr)

        if from_node.type == "UVMAP":
            uv_name = getattr(from_node, "uv_map", None)
            if uv_name:
                return uv_name

        if from_node.type == "TEX_COORD" and link.from_socket and link.from_socket.name == "UV":
            return _guess_active_uv_for_material(mat)

        if from_node.type == "ATTRIBUTE":
            attr_name = getattr(from_node, "attribute_name", None)
            if attr_name:
                return attr_name

        for input_socket in getattr(from_node, "inputs", []):
            resolved = _trace_uv_map_name_from_socket(nt, input_socket, mat, visited_nodes, depth + 1)
            if resolved:
                return resolved

    return None


def _resolve_uv_map_for_image_node(nt, img_node, mat):
    """Resolve UV map name for an Image Texture node, even without explicit UVMap node."""
    if not img_node or img_node.type != "TEX_IMAGE":
        return None

    vector_input = img_node.inputs.get("Vector") if hasattr(img_node, "inputs") else None
    resolved = _trace_uv_map_name_from_socket(nt, vector_input, mat)
    if resolved:
        return resolved

    # If vector input is unlinked, Blender's UV lookup is implicit; use material fallback.
    return _guess_active_uv_for_material(mat)


def _detect_textures_from_node_tree(mat):
    """
    Scan the shader node tree for Image Texture nodes and map them to BlenRose channels.
    Returns a dictionary mapping channel names to (image, uv_node) tuples.
    """
    if not mat or not mat.use_nodes or not mat.node_tree:
        return {}
    
    nt = mat.node_tree
    detected_textures = {}
    
    # Find all Image Texture nodes
    image_nodes = [node for node in nt.nodes if node.type == "TEX_IMAGE" and node.image]
    
    # Channel mapping based on node connections and names
    # Map: (channel_name, detection_keywords, connection_targets)
    channel_detection = [
        ("diffuse", ["diffuse", "base", "color", "albedo"], ["Base Color", "Color"]),
        ("specular", ["specular", "spec"], ["Specular", "Specular IOR Level", "Specular Tint"]),
        ("normal", ["normal", "norm", "nrm"], ["Normal"]),
        ("lightmap", ["lightmap", "light", "lm", "emission"], ["Emission", "Color"]),  # Check emission connection
        ("detail", ["detail", "det"], []),
        ("macro_overlay", ["macro", "overlay"], []),
        ("environment", ["environment", "env", "reflection"], []),
        ("decal", ["decal"], []),
        ("transparent", ["transparent", "alpha"], ["Alpha"]),
        ("noise", ["noise"], []),
    ]
    
    for img_node in image_nodes:
        if not img_node.image:
            continue
        
        # Get node name/label (case-insensitive)
        node_name_lower = (img_node.name + " " + (img_node.label or "")).lower()
        
        # Check where this texture is connected
        connected_targets = []
        for link in nt.links:
            if link.from_node == img_node:
                to_socket = link.to_socket
                if to_socket:
                    connected_targets.append(to_socket.name)
                    # Also check parent node type
                    if to_socket.node:
                        connected_targets.append(to_socket.node.type)
        
        # Try to match to a channel
        best_match = None
        best_score = 0
        
        for channel_name, keywords, targets in channel_detection:
            score = 0
            
            # Check node name/label for keywords
            for keyword in keywords:
                if keyword in node_name_lower:
                    score += 10
            
            # Check connections
            for target in targets:
                if target in connected_targets:
                    score += 20  # Connection is stronger indicator
            
            # Special handling for lightmap (connected to emission)
            if channel_name == "lightmap":
                for link in nt.links:
                    if link.from_node == img_node and link.to_node and link.to_node.type == "EMISSION":
                        score += 30  # Very strong indicator
            
            # Special handling for normal (connected to normal map or normal input)
            if channel_name == "normal":
                for link in nt.links:
                    if link.from_node == img_node:
                        to_node = link.to_node
                        if to_node and (to_node.type == "NORMAL_MAP" or 
                                       (hasattr(to_node, "inputs") and "Normal" in [s.name for s in to_node.inputs])):
                            score += 30
            
            # Special handling for diffuse (connected to base color)
            if channel_name == "diffuse":
                for link in nt.links:
                    if link.from_node == img_node:
                        to_socket = link.to_socket
                        if to_socket and ("Base Color" in to_socket.name or "Color" in to_socket.name):
                            to_node = link.to_node
                            if to_node and to_node.type in ["BSDF_PRINCIPLED", "BSDF_DIFFUSE"]:
                                score += 30
            
            # Special handling for specular (connected to specular input)
            if channel_name == "specular":
                for link in nt.links:
                    if link.from_node == img_node:
                        to_socket = link.to_socket
                        if to_socket and "Specular" in to_socket.name:
                            score += 30
            
            if score > best_score:
                best_score = score
                best_match = channel_name
        
        # If we found a match and haven't already assigned this channel
        if best_match and best_score > 0 and best_match not in detected_textures:
            uv_map_name = _resolve_uv_map_for_image_node(nt, img_node, mat)

            detected_textures[best_match] = {
                "image": img_node.image,
                "uv_node": None,
                "uv_map_name": uv_map_name,
                "image_node": img_node
            }
    
    return detected_textures


def _auto_fill_textures_from_node_tree(settings, update_existing=False):
    """
    Auto-fill BlenRose texture properties from detected Image Texture nodes in the shader.
    
    Args:
        settings: BlenroseMaterialSettings instance
        update_existing: If True, updates even if property already has a value (syncs with node tree).
                        If False, only updates empty properties (default behavior).
    """
    mat = _get_owner_material(settings)
    if not mat:
        return

    mat_ptr = mat.as_pointer()
    
    detected = _detect_textures_from_node_tree(mat)
    
    # Map detected textures to settings properties
    texture_prop_map = {
        "diffuse": "diffuse_tex",
        "lightmap": "lightmap_tex",
        "specular": "specular_tex",
        "normal": "normal_tex",
        "detail": "detail_tex",
        "macro_overlay": "macro_overlay_tex",
        "environment": "environment_tex",
        "decal": "decal_tex",
        "transparent": "transparent_tex",
        "noise": "noise_tex",
    }
    
    uv_prop_map = {
        "diffuse": "diffuse_uv",
        "lightmap": "lightmap_uv",
        "specular": "specular_uv",
        "normal": "normal_uv",
        "detail": "detail_uv",
        "macro_overlay": "macro_overlay_uv",
        "environment": "environment_uv",
        "decal": "decal_uv",
        "transparent": "transparent_uv",
        "noise": "noise_uv",
    }
    
    _SUPPRESSED_UPDATE_MATERIALS.add(mat_ptr)
    try:
        for channel_name, texture_info in detected.items():
            tex_prop = texture_prop_map.get(channel_name)
            uv_prop = uv_prop_map.get(channel_name)
            
            if tex_prop:
                # Update texture (respecting update_existing flag)
                current_image = getattr(settings, tex_prop, None)
                if update_existing or not current_image:
                    setattr(settings, tex_prop, texture_info["image"])
            
            if uv_prop:
                uv_map_name = texture_info.get("uv_map_name")
                if uv_map_name:
                    # Find the UV map in the enum and set it using the identifier string
                    try:
                        uv_enum_items = uv_channel_items(settings, bpy.context)
                        for identifier, name, desc in uv_enum_items:
                            if identifier == uv_map_name or name == uv_map_name:
                                # Set using the identifier string (first element of tuple)
                                setattr(settings, uv_prop, identifier)
                                break
                    except:
                        pass  # Silently fail if context is not available
    finally:
        _SUPPRESSED_UPDATE_MATERIALS.discard(mat_ptr)


def _update_diffuse_has_alpha(self, context):
    """Connect or disconnect diffuse alpha into the BSDF alpha based on flag."""
    mat = _get_owner_material(self)
    if not mat or not mat.use_nodes or not mat.node_tree:
        return
    nt = mat.node_tree
    bsdf = nt.nodes.get("BR_BSDF")
    tex = nt.nodes.get("BR_DiffuseTex")
    if not bsdf or not tex:
        return

    alpha_input = bsdf.inputs.get("Alpha")
    alpha_output = tex.outputs.get("Alpha")
    if not alpha_input or not alpha_output:
        return

    # Remove any existing links into BSDF Alpha
    for link in list(alpha_input.links):
        nt.links.remove(link)

    # If enabled, connect texture alpha to BSDF alpha
    if self.diffuse_has_alpha:
        nt.links.new(alpha_output, alpha_input)


def _update_uv_from_prop(self, node_name, uv_prop):
    """Generic helper to push UV choice into a UV Map node."""
    mat = _get_owner_material(self)
    if not mat or not mat.use_nodes or not mat.node_tree:
        return
    nt = mat.node_tree
    uv_node = nt.nodes.get(node_name)
    if not uv_node or uv_node.type != "UVMAP":
        return
    uv_name = getattr(self, uv_prop, None)
    if uv_name:
        uv_node.uv_map = uv_name


def _update_diffuse_uv(self, context):
    _update_all_blenrose_nodes(self, context)


def _update_lightmap_uv(self, context):
    _update_all_blenrose_nodes(self, context)


def _update_specular_uv(self, context):
    _update_all_blenrose_nodes(self, context)


def _update_normal_uv(self, context):
    _update_all_blenrose_nodes(self, context)


def _update_scalar_values(self, context):
    """Update scalar values in node tree when they change."""
    _update_all_blenrose_nodes(self, context)


def _update_all_blenrose_nodes(self, context):
    """
    Update all Blenrose nodes (textures, UVs, and scalar values) from current settings.
    This ensures all nodes stay in sync when any property changes.
    If critical nodes are missing, rebuild the entire node tree.
    """
    mat = _get_owner_material(self)
    if not mat:
        return

    if mat.as_pointer() in _SUPPRESSED_UPDATE_MATERIALS:
        return
    
    # Ensure nodes are enabled
    if not mat.use_nodes:
        mat.use_nodes = True
    
    # Check if critical nodes exist - if not, rebuild the entire tree
    nt = mat.node_tree
    if not nt:
        return
    
    # Check for critical nodes that must exist
    critical_nodes = ["BR_BSDF", "BR_MixShader", "BR_Emission"]
    missing_critical = False
    for node_name in critical_nodes:
        if not nt.nodes.get(node_name):
            missing_critical = True
            break
    
    # If critical nodes are missing, rebuild the entire tree
    if missing_critical:
        _build_blenrose_node_tree(mat)
        return
    
    # Otherwise, proceed with normal updates
    
    # Update all texture nodes
    texture_mappings = [
        ("BR_DiffuseTex", "diffuse_tex"),
        ("BR_LightmapTex", "lightmap_tex"),
        ("BR_SpecularTex", "specular_tex"),
        ("BR_NormalTex", "normal_tex"),
        ("BR_DetailTex", "detail_tex"),
        ("BR_MacroOverlayTex", "macro_overlay_tex"),
        ("BR_EnvironmentTex", "environment_tex"),
        ("BR_DecalTex", "decal_tex"),
        ("BR_TransparentTex", "transparent_tex"),
        ("BR_NoiseTex", "noise_tex"),
    ]
    
    for node_name, prop_name in texture_mappings:
        tex_node = nt.nodes.get(node_name)
        if tex_node and tex_node.type == "TEX_IMAGE":
            tex_image = getattr(self, prop_name, None)
            tex_node.image = tex_image
    
    # Update all UV map nodes
    uv_mappings = [
        ("BR_DiffuseUV", "diffuse_uv"),
        ("BR_LightmapUV", "lightmap_uv"),
        ("BR_SpecularUV", "specular_uv"),
        ("BR_NormalUV", "normal_uv"),
        ("BR_DetailUV", "detail_uv"),
        ("BR_MacroOverlayUV", "macro_overlay_uv"),
        ("BR_EnvironmentUV", "environment_uv"),
        ("BR_DecalUV", "decal_uv"),
        ("BR_TransparentUV", "transparent_uv"),
        ("BR_NoiseUV", "noise_uv"),
    ]
    
    for node_name, prop_name in uv_mappings:
        uv_node = nt.nodes.get(node_name)
        if uv_node and uv_node.type == "UVMAP":
            uv_name = getattr(self, prop_name, None)
            if uv_name:
                uv_node.uv_map = uv_name
    
    # Update scalar values that affect nodes
    # Detail Normal UV Scale
    detail_mapping = nt.nodes.get("BR_DetailMapping")
    if detail_mapping:
        scale_val = getattr(self, "detail_normal_uv_scale", 8.0)
        detail_mapping.inputs["Scale"].default_value = (scale_val, scale_val, 1.0)
    
    # Macro Overlay UV Scale
    macro_mapping = nt.nodes.get("BR_MacroMapping")
    if macro_mapping:
        scale_val = getattr(self, "macro_overlay_uv_scale", 0.3)
        macro_mapping.inputs["Scale"].default_value = (scale_val, scale_val, 1.0)
    
    # Macro Overlay Opacity
    macro_mix = nt.nodes.get("BR_MacroMix")
    if macro_mix:
        opacity_val = getattr(self, "macro_overlay_opacity", 1.0)
        macro_mix.inputs["Fac"].default_value = opacity_val
    
    # Update Mix Shader Fac to 0.02 (always use this value, regardless of lightmap)
    mix_shader_node = nt.nodes.get("BR_MixShader")
    if mix_shader_node and mix_shader_node.inputs["Fac"].is_linked == False:
        mix_shader_node.inputs["Fac"].default_value = 0.02
    
    # Update lightmap-to-emission connection (always maintain the connection)
    emit_node = nt.nodes.get("BR_Emission")
    lightmap_tex_node = nt.nodes.get("BR_LightmapTex")
    
    if emit_node and lightmap_tex_node:
        # Check if connection already exists
        connection_exists = False
        for link in nt.links:
            if link.from_node == lightmap_tex_node and link.to_node == emit_node and link.from_socket.name == "Color" and link.to_socket.name == "Color":
                connection_exists = True
                break
        
        # Always connect lightmap to emission (even if texture is None, the connection should exist)
        if not connection_exists:
            nt.links.new(lightmap_tex_node.outputs["Color"], emit_node.inputs["Color"])


def _update_diffuse_tex(self, context):
    _update_all_blenrose_nodes(self, context)


def _update_lightmap_tex(self, context):
    _update_all_blenrose_nodes(self, context)


def _update_specular_tex(self, context):
    _update_all_blenrose_nodes(self, context)


def _update_normal_tex(self, context):
    _update_all_blenrose_nodes(self, context)


def _update_detail_tex(self, context):
    _update_all_blenrose_nodes(self, context)


def _update_macro_overlay_tex(self, context):
    _update_all_blenrose_nodes(self, context)


def _update_environment_tex(self, context):
    _update_all_blenrose_nodes(self, context)


def _update_decal_tex(self, context):
    _update_all_blenrose_nodes(self, context)


def _update_transparent_tex(self, context):
    _update_all_blenrose_nodes(self, context)


def _update_noise_tex(self, context):
    _update_all_blenrose_nodes(self, context)


def _build_blenrose_node_tree(mat):
    """
    Build or rebuild the Blenrose node tree for a material.
    Clears existing nodes and creates a complete Blenrose setup.
    """
    if not mat.use_nodes:
        mat.use_nodes = True
    
    nt = mat.node_tree
    
    # Clear all existing nodes
    for node in list(nt.nodes):
        nt.nodes.remove(node)
    
    # Create output node
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    out.location = (600, 0)
    
    # Create BSDF
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.name = "BR_BSDF"
    bsdf.location = (200, 0)
    
    # Final shader output: Mix Shader blends BSDF with lightmap emission
    mix_shader = nt.nodes.new("ShaderNodeMixShader")
    mix_shader.name = "BR_MixShader"
    mix_shader.location = (400, 100)
    
    # Emission shader for lightmap
    emit = nt.nodes.new("ShaderNodeEmission")
    emit.name = "BR_Emission"
    emit.location = (200, 200)
    
    settings = mat.blenrose_settings
    
    # Diffuse
    uv_diff = nt.nodes.new("ShaderNodeUVMap")
    uv_diff.name = "BR_DiffuseUV"
    uv_diff.location = (-800, 200)
    diffuse_uv_name = getattr(settings, "diffuse_uv", None)
    if diffuse_uv_name:
        uv_diff.uv_map = diffuse_uv_name
    
    tex_diff = nt.nodes.new("ShaderNodeTexImage")
    tex_diff.name = "BR_DiffuseTex"
    tex_diff.label = "DiffuseTexture"
    tex_diff.location = (-600, 200)
    tex_diff.image = settings.diffuse_tex
    nt.links.new(uv_diff.outputs["UV"], tex_diff.inputs["Vector"])
    # Don't connect directly - will be connected after macro overlay blending
    base_color_result = tex_diff.outputs["Color"]
    
    # Normal
    uv_norm = nt.nodes.new("ShaderNodeUVMap")
    uv_norm.name = "BR_NormalUV"
    uv_norm.location = (-800, 0)
    normal_uv_name = getattr(settings, "normal_uv", None)
    if normal_uv_name:
        uv_norm.uv_map = normal_uv_name
    
    tex_norm = nt.nodes.new("ShaderNodeTexImage")
    tex_norm.name = "BR_NormalTex"
    tex_norm.label = "NormalTexture"
    tex_norm.location = (-600, 0)
    tex_norm.image = settings.normal_tex
    nt.links.new(uv_norm.outputs["UV"], tex_norm.inputs["Vector"])
    
    normal_map = nt.nodes.new("ShaderNodeNormalMap")
    normal_map.location = (-400, 0)
    normal_map.name = "BR_NormalMap"
    nt.links.new(tex_norm.outputs["Color"], normal_map.inputs["Color"])
    # Don't connect yet - will connect after detail normal blending if needed
    base_normal_result = normal_map.outputs["Normal"]
    
    # Specular
    uv_spec = nt.nodes.new("ShaderNodeUVMap")
    uv_spec.name = "BR_SpecularUV"
    uv_spec.location = (-800, -200)
    specular_uv_name = getattr(settings, "specular_uv", None)
    if specular_uv_name:
        uv_spec.uv_map = specular_uv_name
    
    tex_spec = nt.nodes.new("ShaderNodeTexImage")
    tex_spec.name = "BR_SpecularTex"
    tex_spec.label = "SpecularTexture"
    tex_spec.location = (-600, -200)
    tex_spec.image = settings.specular_tex
    nt.links.new(uv_spec.outputs["UV"], tex_spec.inputs["Vector"])
    
    # Connect specular texture - check if Specular input exists
    specular_input = None
    if "Specular" in bsdf.inputs:
        specular_input = bsdf.inputs["Specular"]
    elif "Specular IOR Level" in bsdf.inputs:
        specular_input = bsdf.inputs["Specular IOR Level"]
    elif "Specular Tint" in bsdf.inputs:
        specular_input = bsdf.inputs["Specular Tint"]
    
    if specular_input is not None:
        separate_rgb = nt.nodes.new("ShaderNodeSeparateRGB")
        separate_rgb.location = (-400, -200)
        separate_rgb.label = "Specular Extract"
        nt.links.new(tex_spec.outputs["Color"], separate_rgb.inputs["Image"])
        nt.links.new(separate_rgb.outputs["R"], specular_input)
    
    # Lightmap as emission contribution
    uv_light = nt.nodes.new("ShaderNodeUVMap")
    uv_light.name = "BR_LightmapUV"
    uv_light.location = (-800, 400)
    lightmap_uv_name = getattr(settings, "lightmap_uv", None)
    if lightmap_uv_name:
        uv_light.uv_map = lightmap_uv_name
    
    tex_light = nt.nodes.new("ShaderNodeTexImage")
    tex_light.name = "BR_LightmapTex"
    tex_light.label = "LightMapTexture"
    tex_light.location = (-600, 400)
    tex_light.image = settings.lightmap_tex
    nt.links.new(uv_light.outputs["UV"], tex_light.inputs["Vector"])
    
    # Always connect lightmap texture Color output to Emission Color input
    nt.links.new(tex_light.outputs["Color"], emit.inputs["Color"])
    
    # Always connect Emission to Mix Shader input 2 (second shader)
    nt.links.new(emit.outputs["Emission"], mix_shader.inputs[2])
    
    # Set Mix Shader Fac to 0.02 (always use this value)
    mix_shader.inputs["Fac"].default_value = 0.02
    
    # Detail Normal with UV Scale
    detail_normal_result = None
    if settings.detail_tex:
        uv_detail = nt.nodes.new("ShaderNodeUVMap")
        uv_detail.name = "BR_DetailUV"
        uv_detail.location = (-1000, -100)
        detail_uv_name = getattr(settings, "detail_uv", None)
        if detail_uv_name:
            uv_detail.uv_map = detail_uv_name
        
        # Apply detailNormalUVScale using Mapping node
        detail_mapping = nt.nodes.new("ShaderNodeMapping")
        detail_mapping.name = "BR_DetailMapping"
        detail_mapping.location = (-800, -100)
        detail_mapping.inputs["Scale"].default_value = (settings.detail_normal_uv_scale, settings.detail_normal_uv_scale, 1.0)
        nt.links.new(uv_detail.outputs["UV"], detail_mapping.inputs["Vector"])
        
        tex_detail = nt.nodes.new("ShaderNodeTexImage")
        tex_detail.name = "BR_DetailTex"
        tex_detail.label = "DetailTexture"
        tex_detail.location = (-600, -100)
        tex_detail.image = settings.detail_tex
        nt.links.new(detail_mapping.outputs["Vector"], tex_detail.inputs["Vector"])
        
        # Blend detail normal with base normal
        detail_normal_map = nt.nodes.new("ShaderNodeNormalMap")
        detail_normal_map.location = (-400, -100)
        detail_normal_map.label = "Detail Normal Map"
        nt.links.new(tex_detail.outputs["Color"], detail_normal_map.inputs["Color"])
        
        # Mix detail normal with base normal (blend them together)
        normal_mix = nt.nodes.new("ShaderNodeMixRGB")
        normal_mix.location = (-200, 0)
        normal_mix.name = "BR_NormalMix"
        normal_mix.blend_type = "MIX"
        normal_mix.inputs["Fac"].default_value = 0.5  # 50% blend of detail over base
        nt.links.new(base_normal_result, normal_mix.inputs["Color1"])
        nt.links.new(detail_normal_map.outputs["Normal"], normal_mix.inputs["Color2"])
        base_normal_result = normal_mix.outputs["Color"]
    
    # Connect final normal (either base or blended with detail)
    nt.links.new(base_normal_result, bsdf.inputs["Normal"])
    
    # Macro Overlay with UV Scale and Opacity
    if settings.macro_overlay_tex:
        uv_macro = nt.nodes.new("ShaderNodeUVMap")
        uv_macro.name = "BR_MacroOverlayUV"
        uv_macro.location = (-1000, 300)
        macro_uv_name = getattr(settings, "macro_overlay_uv", None)
        if macro_uv_name:
            uv_macro.uv_map = macro_uv_name
        
        # Apply macroOverlayUVScale using Mapping node
        macro_mapping = nt.nodes.new("ShaderNodeMapping")
        macro_mapping.name = "BR_MacroMapping"
        macro_mapping.location = (-800, 300)
        macro_mapping.inputs["Scale"].default_value = (settings.macro_overlay_uv_scale, settings.macro_overlay_uv_scale, 1.0)
        nt.links.new(uv_macro.outputs["UV"], macro_mapping.inputs["Vector"])
        
        tex_macro = nt.nodes.new("ShaderNodeTexImage")
        tex_macro.name = "BR_MacroOverlayTex"
        tex_macro.label = "MacroOverlayTexture"
        tex_macro.location = (-600, 300)
        tex_macro.image = settings.macro_overlay_tex
        nt.links.new(macro_mapping.outputs["Vector"], tex_macro.inputs["Vector"])
        
        # Blend macro overlay with base color using macroOverlayOpacity
        macro_mix = nt.nodes.new("ShaderNodeMixRGB")
        macro_mix.name = "BR_MacroMix"
        macro_mix.location = (-400, 250)
        macro_mix.blend_type = "MIX"
        macro_mix.inputs["Fac"].default_value = settings.macro_overlay_opacity
        nt.links.new(base_color_result, macro_mix.inputs["Color1"])
        nt.links.new(tex_macro.outputs["Color"], macro_mix.inputs["Color2"])
        base_color_result = macro_mix.outputs["Color"]
    
    # Connect final base color (with macro overlay blended)
    nt.links.new(base_color_result, bsdf.inputs["Base Color"])
    
    # Connect Principled BSDF to Mix Shader input 1 (first shader)
    nt.links.new(bsdf.outputs["BSDF"], mix_shader.inputs[1])
    # Mix Shader output goes to Material Output Surface
    nt.links.new(mix_shader.outputs["Shader"], out.inputs["Surface"])


def _update_enabled(self, context):
    """Update function for enabled property - creates or removes node tree."""
    mat = _get_owner_material(self)
    if not mat:
        return
    
    if self.enabled:
        # Before building, try to auto-detect textures from existing node tree
        # This allows users to set up textures first, then enable Blenrose
        if mat.use_nodes and mat.node_tree:
            _auto_fill_textures_from_node_tree(self)
        
        # Build the node tree
        _build_blenrose_node_tree(mat)
    else:
        # Remove Blenrose nodes if material uses nodes
        if mat.use_nodes and mat.node_tree:
            nt = mat.node_tree
            # Remove nodes that start with "BR_"
            nodes_to_remove = [node for node in nt.nodes if node.name.startswith("BR_")]
            for node in nodes_to_remove:
                nt.nodes.remove(node)


def _load_material_classes_from_xml():
    """Kept for compatibility; no-op now that classes are hard-coded."""
    return


def material_subclass_items(self, context):
    """
    Enum callback for material_subclass.

    Blender passes the *instance* of BlenroseMaterialSettings as `self`, so we
    can look at `self.material_class` and filter subclasses to only those
    matching the current class.
    """
    material_class = getattr(self, "material_class", None)
    if material_class and material_class in MATERIAL_SUBCLASSES:
        return MATERIAL_SUBCLASSES[material_class]
    # Safety fallback so registration never fails even if something is off.
    return MATERIAL_SUBCLASS_FALLBACK


# -------------------------------------------------------------------------
# Per‑material Blenrose properties
# -------------------------------------------------------------------------

class BlenroseMaterialSettings(PropertyGroup):
    """Custom properties stored per Blender material."""

    enabled: BoolProperty(
        name="Enable Blenrose",
        description="Enable Blenrose editing for this material",
        default=False,
        update=_update_enabled,
    )

    # High‑level material classification
    material_class: EnumProperty(
        name="Material Class",
        items=MATERIAL_CLASS_ITEMS,
        default="ENVIRONMENT",
    )

    material_subclass: EnumProperty(
        name="Subclass",
        items=material_subclass_items,
        # When 'items' is a function, Blender expects the default to be an
        # integer index into the items list, not an identifier string.
        # 0 => first entry in whatever list material_subclass_items returns.
        default=0,
    )

    notes: StringProperty(
        name="Notes",
        description="Freeform notes for this material",
        default="",
    )

    # --- Texture channels (presentation) ---
    # Order: Diffuse, LightMap, Specular, Normal

    diffuse_tex: PointerProperty(
        name="DiffuseTexture",
        type=bpy.types.Image,
        update=_update_diffuse_tex,
    )
    diffuse_uv: EnumProperty(
        name="UV",
        items=uv_channel_items,
        default=0,  # UV0
        update=_update_diffuse_uv,
    )

    lightmap_tex: PointerProperty(
        name="LightMapTexture",
        type=bpy.types.Image,
        update=_update_lightmap_tex,
    )
    lightmap_uv: EnumProperty(
        name="UV",
        items=uv_channel_items,
        default=1,  # UV1
        update=_update_lightmap_uv,
    )

    specular_tex: PointerProperty(
        name="SpecularTexture",
        type=bpy.types.Image,
        update=_update_specular_tex,
    )
    specular_uv: EnumProperty(
        name="UV",
        items=uv_channel_items,
        default=0,  # UV0
        update=_update_specular_uv,
    )

    normal_tex: PointerProperty(
        name="NormalTexture",
        type=bpy.types.Image,
        update=_update_normal_tex,
    )
    normal_uv: EnumProperty(
        name="UV",
        items=uv_channel_items,
        default=0,  # UV0
        update=_update_normal_uv,
    )

    detail_tex: PointerProperty(
        name="DetailTexture",
        type=bpy.types.Image,
        update=_update_detail_tex,
    )
    detail_uv: EnumProperty(
        name="UV",
        items=uv_channel_items,
        default=0,  # UV0
        update=_update_all_blenrose_nodes,
    )

    macro_overlay_tex: PointerProperty(
        name="MacroOverlayTexture",
        type=bpy.types.Image,
        update=_update_macro_overlay_tex,
    )
    macro_overlay_uv: EnumProperty(
        name="UV",
        items=uv_channel_items,
        default=0,  # UV0
        update=_update_all_blenrose_nodes,
    )

    environment_tex: PointerProperty(
        name="EnvironmentTexture",
        type=bpy.types.Image,
        update=_update_environment_tex,
    )
    environment_uv: EnumProperty(
        name="UV",
        items=uv_channel_items,
        default=0,  # UV0
        update=_update_all_blenrose_nodes,
    )

    decal_tex: PointerProperty(
        name="DecalTexture",
        type=bpy.types.Image,
        update=_update_decal_tex,
    )
    decal_uv: EnumProperty(
        name="UV",
        items=uv_channel_items,
        default=0,  # UV0
        update=_update_all_blenrose_nodes,
    )

    transparent_tex: PointerProperty(
        name="TransparentTexture",
        type=bpy.types.Image,
        update=_update_transparent_tex,
    )
    transparent_uv: EnumProperty(
        name="UV",
        items=uv_channel_items,
        default=0,  # UV0
        update=_update_all_blenrose_nodes,
    )

    noise_tex: PointerProperty(
        name="NoiseTexture",
        type=bpy.types.Image,
        update=_update_noise_tex,
    )
    noise_uv: EnumProperty(
        name="UV",
        items=uv_channel_items,
        default=0,  # UV0
        update=_update_all_blenrose_nodes,
    )

    # --- Scalars (presentation) ---

    detail_normal_uv_scale: FloatProperty(
        name="DetailNormalUVScale",
        default=8.0,
        update=_update_scalar_values,
    )

    macro_overlay_opacity: FloatProperty(
        name="MacroOverlayOpacity",
        default=1.0,
        min=0.0,
        max=1.0,
        update=_update_scalar_values,
    )

    macro_overlay_uv_scale: FloatProperty(
        name="MacroOverlayUVScale",
        default=0.3,  # PSG default is 0.3
        update=_update_scalar_values,
    )

    embedded_decal: FloatProperty(
        name="EmbeddedDecal",
        default=1.0,
        min=0.0,
        max=1.0,
        update=_update_scalar_values,
    )

    # --- Collision‑mapped channels ---

    physics_surface: EnumProperty(
        name="PhysicsSurface",
        description="Physics behavior (player-surface interaction)",
        items=PHYSICS_SURFACE_ITEMS,
        default='0',
    )

    audio_surface: EnumProperty(
        name="AudioSurface",
        description="Audio/grind material type (controls sound and grinding behavior)",
        items=AUDIO_SURFACE_ITEMS,
        default='0',
    )

    surface_pattern: EnumProperty(
        name="SurfacePattern",
        description="Surface texture pattern (visual/audio variation)",
        items=SURFACE_PATTERN_ITEMS,
        default='0',
    )


# -------------------------------------------------------------------------
# Material list UI
# -------------------------------------------------------------------------

class BLENROSE_UL_materials(UIList):
    """Simple material list with indication if Blenrose is enabled."""

    def draw_item(
        self, context, layout, data, item, icon, active_data, active_propname, index
    ):
        mat = item
        if mat is None:
            return

        settings = mat.blenrose_settings

        row = layout.row(align=True)
        icon_id = "MATERIAL"

        if settings.enabled:
            row.prop(mat, "name", text="", emboss=False, icon=icon_id)
        else:
            # Greyed out name if not enabled
            sub = row.row(align=True)
            sub.enabled = False
            sub.prop(mat, "name", text="", emboss=False, icon=icon_id)


# -------------------------------------------------------------------------
# Operator: create new Blenrose material with custom node tree
# -------------------------------------------------------------------------

class BLENROSE_OT_new_material(Operator):
    """Create a new Blenrose material with a basic node setup"""

    bl_idname = "blenrose.new_material"
    bl_label = "New Blenrose Material"
    bl_options = {"REGISTER", "UNDO"}

    base_name: StringProperty(
        name="Name",
        default="BlenroseMaterial",
    )

    def execute(self, context):
        mat = bpy.data.materials.new(self.base_name)
        mat.use_nodes = True

        # Build the node tree using the shared function
        _build_blenrose_node_tree(mat)

        # Enable Blenrose settings
        settings = mat.blenrose_settings
        settings.enabled = True

        # Make it the active material in our editor
        scene = context.scene
        try:
            idx = list(bpy.data.materials).index(mat)
            scene.blenrose_mat_index = idx
        except ValueError:
            pass

        self.report({"INFO"}, f"Created material '{mat.name}'")
        return {"FINISHED"}

    def invoke(self, context, event):
        return context.window_manager.invoke_props_dialog(self, width=320)


# -------------------------------------------------------------------------
# Operator: delete material
# -------------------------------------------------------------------------

class BLENROSE_OT_delete_material(Operator):
    """Delete the active material"""

    bl_idname = "blenrose.delete_material"
    bl_label = "Delete Material"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        mats = bpy.data.materials
        if not mats:
            self.report({"ERROR"}, "No materials to delete")
            return {"CANCELLED"}

        scene = context.scene
        idx = scene.blenrose_mat_index
        idx = max(0, min(idx, len(mats) - 1))
        mat = mats[idx]

        # Store name for report
        mat_name = mat.name

        # Delete the material
        bpy.data.materials.remove(mat)

        # Adjust index if necessary
        if len(bpy.data.materials) > 0:
            scene.blenrose_mat_index = min(idx, len(bpy.data.materials) - 1)
        else:
            scene.blenrose_mat_index = 0

        self.report({"INFO"}, f"Deleted material '{mat_name}'")
        return {"FINISHED"}


# -------------------------------------------------------------------------
# Main editor operator (popup window)
# -------------------------------------------------------------------------

class BLENROSE_OT_material_editor(Operator):
    """Melrose-like material editor"""

    bl_idname = "blenrose.material_editor"
    bl_label = "Blenrose Material Editor"
    bl_options = {"REGISTER"}

    def draw_channel_row_texture(self, layout, label, img_prop, uv_prop, settings):
        row = layout.row()
        row.label(text=label)
        row.prop(settings, img_prop, text="")
        row.prop(settings, uv_prop, text="")

    def draw(self, context):
        layout = self.layout
        scene = context.scene

        # --- Material list ---
        row = layout.row()
        row.template_list(
            "BLENROSE_UL_materials",
            "",
            bpy.data,
            "materials",
            scene,
            "blenrose_mat_index",
            rows=6,
        )

        col = row.column(align=True)
        col.operator("blenrose.new_material", icon="ADD", text="")
        col.operator("blenrose.delete_material", icon="REMOVE", text="")
        col.separator()
        col.operator(
            "material.new", icon="MATERIAL_DATA", text=""
        )  # vanilla material button
        
        # Enable all + prepare for export
        layout.separator()
        layout.operator("blenrose.enable_all_and_prepare_export", icon="SHADERFX", text="Enable Blenrose for All & Prepare for Export")
        layout.separator()
        layout.operator("blenrose.bulk_export", icon="EXPORT", text="Bulk Export All Objects")

        # Resolve active material
        mats = bpy.data.materials
        if not mats:
            layout.label(text="No materials in this file.", icon="ERROR")
            return

        idx = scene.blenrose_mat_index
        idx = max(0, min(idx, len(mats) - 1))
        mat = mats[idx]

        settings = mat.blenrose_settings

        layout.separator()
        box = layout.box()
        row = box.row()
        row.prop(settings, "enabled", text="Enable Blenrose for this material")
        row.label(text=f"Active: {mat.name}", icon="MATERIAL")

        if not settings.enabled:
            box.label(
                text="(Enable Blenrose to edit Skate 3 channels for this material.)",
                icon="INFO",
            )
            return

        # --- Material class / subclass ---
        row = box.row()
        row.prop(settings, "material_class", text="Material Class")
        row.prop(settings, "material_subclass", text="Subclass")

        box.separator()

        # --- Channel table header ---
        col = box.column()
        header = col.row()
        header.label(text="Channel Name")
        header.label(text="Channel Value")
        header.label(text="Description")

        # --- Texture channels (PSG order) ---
        # Diffuse Texture
        row = col.row()
        row.label(text="Diffuse Texture")
        row.prop(settings, "diffuse_tex", text="")
        row.prop(settings, "diffuse_uv", text="")

        # LightMapTexture
        row = col.row()
        row.label(text="LightMapTexture")
        row.prop(settings, "lightmap_tex", text="")
        row.prop(settings, "lightmap_uv", text="")

        # SpecularTexture
        row = col.row()
        row.label(text="SpecularTexture")
        row.prop(settings, "specular_tex", text="")
        row.prop(settings, "specular_uv", text="")

        # Normal Texture
        row = col.row()
        row.label(text="Normal Texture")
        row.prop(settings, "normal_tex", text="")
        row.prop(settings, "normal_uv", text="")

        # Detail Texture
        row = col.row()
        row.label(text="Detail Texture")
        row.prop(settings, "detail_tex", text="")
        row.prop(settings, "detail_uv", text="")

        # MacroOverlay Texture
        row = col.row()
        row.label(text="MacroOverlay Texture")
        row.prop(settings, "macro_overlay_tex", text="")
        row.prop(settings, "macro_overlay_uv", text="")

        # Environment Texture
        row = col.row()
        row.label(text="Environment Texture")
        row.prop(settings, "environment_tex", text="")
        row.prop(settings, "environment_uv", text="")

        # Decal Texture
        row = col.row()
        row.label(text="Decal Texture")
        row.prop(settings, "decal_tex", text="")
        row.prop(settings, "decal_uv", text="")

        # Transparent Texture
        row = col.row()
        row.label(text="Transparent Texture")
        row.prop(settings, "transparent_tex", text="")
        row.prop(settings, "transparent_uv", text="")

        # Noise Texture
        row = col.row()
        row.label(text="Noise Texture")
        row.prop(settings, "noise_tex", text="")
        row.prop(settings, "noise_uv", text="")

        # --- Collision-mapped rows ---
        col.separator()
        row = col.row()
        row.label(text="PhysicsSurface")
        row.prop(settings, "physics_surface", text="")
        row.label(text="Undefined")

        row = col.row()
        row.label(text="AudioSurface")
        row.prop(settings, "audio_surface", text="")
        row.label(text="Undefined")

        row = col.row()
        row.label(text="SurfacePattern")
        row.prop(settings, "surface_pattern", text="")
        row.label(text="Undefined")

        # --- Scalars ---
        col.separator()
        row = col.row()
        row.label(text="DetailNormalUVScale")
        row.prop(settings, "detail_normal_uv_scale", text="")
        row.label(text="Default 8.0")

        row = col.row()
        row.label(text="MacroOverlayOpacity")
        row.prop(settings, "macro_overlay_opacity", text="")
        row.label(text="Default 1.0")

        row = col.row()
        row.label(text="MacroOverlayUVScale")
        row.prop(settings, "macro_overlay_uv_scale", text="")
        row.label(text="Default 0.3")

        row = col.row()
        row.label(text="EmbeddedDecal")
        row.prop(settings, "embedded_decal", text="")
        row.label(text="Default 1.0")

        # Notes
        box.separator()
        box.label(text="Notes:")
        box.prop(settings, "notes", text="")
        
        # Auto-detect textures button
        box.separator()
        row = box.row()
        row.operator("blenrose.auto_detect_textures", icon="VIEWZOOM", text="Auto-detect Textures from Node Tree")

    def invoke(self, context, event):
        # Large dialog window; Blender doesn't support arbitrary OS windows from Python,
        # but this behaves like a popup editor.
        return context.window_manager.invoke_props_dialog(self, width=800)

    def execute(self, context):
        """
        Needed when using invoke_props_dialog; without execute() the OK button
        does nothing and Blender prints 'Operator cannot redo'.
        We don't need to perform any action on OK, just close the dialog.
        """
        return {"FINISHED"}


# -------------------------------------------------------------------------
# Bulk Export Operator
# -------------------------------------------------------------------------

def _calculate_bbox_from_points(points):
    """Calculate bounding box from a list of (x, y, z) points."""
    if not points:
        return None
    
    min_x = min(p[0] for p in points)
    max_x = max(p[0] for p in points)
    min_y = min(p[1] for p in points)
    max_y = max(p[1] for p in points)
    min_z = min(p[2] for p in points)
    max_z = max(p[2] for p in points)
    
    return {
        "min": [min_x, min_y, min_z],
        "max": [max_x, max_y, max_z],
    }


def _calculate_bbox_for_objects(objects):
    """Calculate combined bounding box for a collection of mesh objects."""
    if not objects:
        return None
    
    min_x = min_y = min_z = float('inf')
    max_x = max_y = max_z = float('-inf')
    
    for obj in objects:
        if obj.type != 'MESH':
            continue
        
        # Get world space bounding box
        # Convert bound_box corners to Vector before matrix multiplication
        from mathutils import Vector
        bbox_corners = [obj.matrix_world @ Vector(obj.bound_box[i]) for i in range(8)]
        
        for corner in bbox_corners:
            # COORDINATE TRANSFORM: Blender → PSG Game Space
            # Blender (X, Y, Z) → PSG (X, Z, -Y)
            psg_x = corner.x
            psg_y = corner.z
            psg_z = -corner.y
            
            min_x = min(min_x, psg_x)
            max_x = max(max_x, psg_x)
            min_y = min(min_y, psg_y)
            max_y = max(max_y, psg_y)
            min_z = min(min_z, psg_z)
            max_z = max(max_z, psg_z)
    
    if min_x == float('inf'):
        return None
    
    return {
        "min": [min_x, min_y, min_z],
        "max": [max_x, max_y, max_z],
    }


def _bbox_intersects(bbox1, bbox2):
    """Check if two bounding boxes intersect."""
    if not bbox1 or not bbox2:
        return False
    
    # Check if boxes overlap on all axes
    return (
        bbox1["min"][0] <= bbox2["max"][0] and bbox1["max"][0] >= bbox2["min"][0] and
        bbox1["min"][1] <= bbox2["max"][1] and bbox1["max"][1] >= bbox2["min"][1] and
        bbox1["min"][2] <= bbox2["max"][2] and bbox1["max"][2] >= bbox2["min"][2]
    )


def _extract_splines_from_scene(context):
    """Extract all splines from curve objects in the scene."""
    splines = []
    curve_objects = [obj for obj in context.scene.objects if obj.type == 'CURVE' and obj.visible_get()]
    
    for obj in curve_objects:
        curve_data = obj.data
        matrix_world = obj.matrix_world
        
        # Process each spline in the curve object
        for spline in curve_data.splines:
            points = []
            
            # Get spline points based on type
            if spline.type == 'POLY' and spline.points:
                for point in spline.points:
                    # Transform to world space
                    co_world = matrix_world @ point.co.xyz
                    blender_x, blender_y, blender_z = co_world.x, co_world.y, co_world.z
                    
                    # COORDINATE TRANSFORM: Blender → PSG Game Space
                    # Blender (X, Y, Z) → PSG (X, Z, -Y)
                    psg_x = blender_x
                    psg_y = blender_z
                    psg_z = -blender_y
                    
                    points.append([psg_x, psg_y, psg_z])
                    
            elif spline.type == 'BEZIER' and spline.bezier_points:
                for bezier_point in spline.bezier_points:
                    # Transform to world space
                    co_world = matrix_world @ bezier_point.co
                    blender_x, blender_y, blender_z = co_world.x, co_world.y, co_world.z
                    
                    # COORDINATE TRANSFORM: Blender → PSG Game Space
                    psg_x = blender_x
                    psg_y = blender_z
                    psg_z = -blender_y
                    
                    points.append([psg_x, psg_y, psg_z])
                    
            elif spline.type == 'NURBS' and spline.points:
                for point in spline.points:
                    # Transform to world space
                    co_world = matrix_world @ point.co.xyz
                    blender_x, blender_y, blender_z = co_world.x, co_world.y, co_world.z
                    
                    # COORDINATE TRANSFORM: Blender → PSG Game Space
                    psg_x = blender_x
                    psg_y = blender_z
                    psg_z = -blender_y
                    
                    points.append([psg_x, psg_y, psg_z])
            
            # Only add splines with at least 2 points
            if len(points) >= 2:
                bbox = _calculate_bbox_from_points(points)
                splines.append({
                    "name": obj.name,
                    "points": points,
                    "is_closed": spline.use_cyclic_u,
                    "type": spline.type,
                    "bbox": bbox,
                })
    
    return splines


def _build_textures_dict(settings, get_image_path, get_uv_name):
    """Build textures dict with only channels that have a texture (file path or embedded)."""
    channel_configs = [
        ("diffuse", "diffuse_tex", "diffuse_uv"),
        ("lightmap", "lightmap_tex", "lightmap_uv"),
        ("specular", "specular_tex", "specular_uv"),
        ("normal", "normal_tex", "normal_uv"),
        ("detail", "detail_tex", "detail_uv"),
        ("macro_overlay", "macro_overlay_tex", "macro_overlay_uv"),
        ("environment", "environment_tex", "environment_uv"),
        ("decal", "decal_tex", "decal_uv"),
        ("transparent", "transparent_tex", "transparent_uv"),
        ("noise", "noise_tex", "noise_uv"),
    ]
    textures = {}
    for channel_name, tex_prop, uv_prop in channel_configs:
        tex = getattr(settings, tex_prop, None)
        path = get_image_path(tex)
        if path or tex:
            textures[channel_name] = {
                "image_path": path,
                "uv": get_uv_name(settings, uv_prop),
            }
    return textures


def _extract_blenrose_material_data(mat):
    """Extract all BlenRose material settings into a dictionary."""
    settings = mat.blenrose_settings
    
    # Get texture image filepaths
    def get_image_path(image):
        if image and image.filepath:
            return bpy.path.abspath(image.filepath)
        return None
    
    # Get UV channel name (enum returns index, we need to resolve the actual name)
    def get_uv_name(settings, prop_name):
        uv_enum_items = uv_channel_items(settings, bpy.context)
        uv_index = getattr(settings, prop_name, 0)
        
        # Handle both string (identifier) and int (index) cases
        if isinstance(uv_index, str):
            # If it's a string, find the index in the items list
            for idx, (identifier, name, desc) in enumerate(uv_enum_items):
                if identifier == uv_index:
                    uv_index = idx
                    break
            else:
                uv_index = 0  # Default to 0 if not found
        
        # Ensure it's an integer and within bounds
        if isinstance(uv_index, int) and uv_index < len(uv_enum_items):
            return uv_enum_items[uv_index][0]  # Return the identifier
        return "UVMap"
    
    # Resolve material subclass name from index
    material_class = settings.material_class
    material_subclass_name = "default"
    if material_class in MATERIAL_SUBCLASSES:
        subclass_items = MATERIAL_SUBCLASSES[material_class]
        subclass_index = getattr(settings, "material_subclass", 0)
        
        # Handle both string (identifier) and int (index) cases
        if isinstance(subclass_index, str):
            # If it's a string, find the index in the items list
            for idx, (identifier, name, desc) in enumerate(subclass_items):
                if identifier == subclass_index:
                    subclass_index = idx
                    break
            else:
                subclass_index = 0  # Default to 0 if not found
        
        # Ensure it's an integer and within bounds
        if isinstance(subclass_index, int) and subclass_index < len(subclass_items):
            material_subclass_name = subclass_items[subclass_index][0]  # Get identifier
    
    # Real files use all-lowercase material name: "class.subclass" (e.g. environment.default, environment.decal_simple)
    material_name_export = f"{material_class.lower()}.{material_subclass_name.lower()}"
    
    data = {
        "material_name": material_name_export,
        "enabled": settings.enabled,
        "material_class": settings.material_class,
        "material_subclass": material_subclass_name,
        "notes": settings.notes,
        "textures": _build_textures_dict(settings, get_image_path, get_uv_name),
        "scalars": {
            "detail_normal_uv_scale": settings.detail_normal_uv_scale,
            "macro_overlay_opacity": settings.macro_overlay_opacity,
            "macro_overlay_uv_scale": settings.macro_overlay_uv_scale,
            "embedded_decal": settings.embedded_decal,
        },
        "collision": {
            "physics_surface": settings.physics_surface,
            "audio_surface": settings.audio_surface,
            "surface_pattern": settings.surface_pattern,
        },
    }
    return data


class BLENROSE_OT_auto_detect_textures(Operator):
    """Auto-detect textures from shader node tree and populate BlenRose settings"""
    
    bl_idname = "blenrose.auto_detect_textures"
    bl_label = "Auto-detect Textures"
    bl_options = {"REGISTER", "UNDO"}
    
    def execute(self, context):
        scene = context.scene
        mats = bpy.data.materials
        if not mats:
            self.report({"WARNING"}, "No materials in this file")
            return {"CANCELLED"}
        
        idx = scene.blenrose_mat_index
        idx = max(0, min(idx, len(mats) - 1))
        mat = mats[idx]
        
        settings = mat.blenrose_settings
        if not settings.enabled:
            self.report({"WARNING"}, "Enable Blenrose for this material first")
            return {"CANCELLED"}
        
        # Auto-detect textures (this will only fill empty properties)
        detected_count = 0
        detected = _detect_textures_from_node_tree(mat)
        
        texture_prop_map = {
            "diffuse": "diffuse_tex",
            "lightmap": "lightmap_tex",
            "specular": "specular_tex",
            "normal": "normal_tex",
            "detail": "detail_tex",
            "macro_overlay": "macro_overlay_tex",
            "environment": "environment_tex",
            "decal": "decal_tex",
            "transparent": "transparent_tex",
            "noise": "noise_tex",
        }
        
        uv_prop_map = {
            "diffuse": "diffuse_uv",
            "lightmap": "lightmap_uv",
            "specular": "specular_uv",
            "normal": "normal_uv",
            "detail": "detail_uv",
            "macro_overlay": "macro_overlay_uv",
            "environment": "environment_uv",
            "decal": "decal_uv",
            "transparent": "transparent_uv",
            "noise": "noise_uv",
        }
        
        for channel_name, texture_info in detected.items():
            tex_prop = texture_prop_map.get(channel_name)
            uv_prop = uv_prop_map.get(channel_name)
            
            if tex_prop:
                # Update texture (always sync with node tree when manually triggered)
                setattr(settings, tex_prop, texture_info["image"])
                detected_count += 1
            
            if uv_prop:
                uv_map_name = texture_info.get("uv_map_name")
                if uv_map_name:
                    uv_enum_items = uv_channel_items(settings, context)
                    for identifier, name, desc in uv_enum_items:
                        if identifier == uv_map_name or name == uv_map_name:
                            # Set using the identifier string (first element of tuple)
                            setattr(settings, uv_prop, identifier)
                            break
        
        if detected_count > 0:
            self.report({"INFO"}, f"Detected and updated {detected_count} texture(s)")
        else:
            self.report({"INFO"}, "No textures detected in node tree")
        
        return {"FINISHED"}


class BLENROSE_OT_enable_all_and_prepare_export(Operator):
    """Enable Blenrose for all materials and order them for export"""

    bl_idname = "blenrose.enable_all_and_prepare_export"
    bl_label = "Enable Blenrose for All & Prepare for Export"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        mats = bpy.data.materials
        if not mats:
            self.report({"WARNING"}, "No materials in this file")
            return {"CANCELLED"}

        enabled_count = 0
        for mat in mats:
            settings = mat.blenrose_settings
            if settings.enabled:
                continue

            # Auto-detect textures from existing node tree before enabling
            if mat.use_nodes and mat.node_tree:
                _auto_fill_textures_from_node_tree(settings)

            # Set sensible defaults for level export
            settings.material_class = "ENVIRONMENT"
            if "ENVIRONMENT" in MATERIAL_SUBCLASSES:
                # Set subclass to DEFAULT (Blender EnumProperty expects identifier string)
                settings.material_subclass = "DEFAULT"

            # Enable Blenrose (builds node tree)
            settings.enabled = True
            enabled_count += 1

        self.report(
            {"INFO"},
            f"Enabled Blenrose for {enabled_count} material(s).",
        )
        return {"FINISHED"}


class BLENROSE_OT_bulk_export(Operator):
    """Export all objects in the scene as GLB files with BlenRose material data"""

    bl_idname = "blenrose.bulk_export"
    bl_label = "Bulk Export All Objects"
    bl_options = {"REGISTER", "UNDO"}

    filepath: StringProperty(
        name="Export Directory",
        description="Directory where GLB files and material data will be exported",
        subtype="DIR_PATH",
    )

    def execute(self, context):
        export_dir = bpy.path.abspath(self.filepath)
        os.makedirs(export_dir, exist_ok=True)

        # Get all mesh objects in the scene
        mesh_objects = [obj for obj in context.scene.objects if obj.type == "MESH"]
        
        if not mesh_objects:
            self.report({"WARNING"}, "No mesh objects found in scene")
            return {"CANCELLED"}

        exported_objects = 0
        exported_materials = {}
        failed = 0

        # Extract all splines from the scene
        all_splines = _extract_splines_from_scene(context)

        # Split objects by material and group the split pieces by material
        # Note: This modifies the scene by splitting multi-material objects.
        # Use Blender's undo (Ctrl+Z) to restore original objects if needed.
        material_groups = {}
        material_group_bboxes = {}
        split_objects = []  # Track objects created by splitting
        
        # Create a temporary collection or use existing scene
        for obj in mesh_objects:
            if not obj.visible_get():
                continue
            
            # Check if object has multiple materials
            materials = [slot.material for slot in obj.material_slots if slot.material]
            
            if not materials:
                # Object with no material - add directly
                material_key = None
                material_groups.setdefault(material_key, []).append(obj)
            elif len(materials) == 1:
                # Single material - add directly to that material group
                material_groups.setdefault(materials[0], []).append(obj)
            else:
                # Multiple materials - split by material
                # Make object active and enter edit mode
                bpy.ops.object.select_all(action="DESELECT")
                obj.select_set(True)
                context.view_layer.objects.active = obj
                
                # Enter edit mode and split by material
                bpy.ops.object.mode_set(mode='EDIT')
                bpy.ops.mesh.select_all(action='SELECT')
                bpy.ops.mesh.separate(type='MATERIAL')
                bpy.ops.object.mode_set(mode='OBJECT')
                
                # Get the separated objects (original object is now one of the pieces)
                # The separate operation creates new objects, all selected after operation
                separated_objects = [o for o in context.selected_objects if o.type == 'MESH']
                
                # Track newly created objects (all except the original)
                for sep_obj in separated_objects:
                    if sep_obj != obj:
                        split_objects.append(sep_obj)
                
                # Group separated objects by their material
                # Each separated piece should have only one material
                for sep_obj in separated_objects:
                    # Get the material for this separated object
                    sep_materials = [slot.material for slot in sep_obj.material_slots if slot.material]
                    if sep_materials:
                        # Each separated piece should have one material
                        material = sep_materials[0]
                        material_groups.setdefault(material, []).append(sep_obj)
                    else:
                        material_groups.setdefault(None, []).append(sep_obj)
        
        # Calculate bounding boxes for each material group (using split objects)
        for material_key, objects in material_groups.items():
            bbox = _calculate_bbox_for_objects(objects)
            if bbox:
                material_group_bboxes[material_key] = bbox
        
        # Assign splines to material groups based on bounding box intersection
        # Map: material_key -> list of splines assigned to that material
        material_splines = {}
        for material_key in material_groups.keys():
            material_splines[material_key] = []
        
        for spline in all_splines:
            spline_bbox = spline.get("bbox")
            if not spline_bbox:
                continue
            
            # Find the material group whose bounding box intersects with this spline
            # If multiple match, use the first one (or could use closest center point)
            assigned = False
            for material_key, group_bbox in material_group_bboxes.items():
                if _bbox_intersects(spline_bbox, group_bbox):
                    material_splines[material_key].append(spline)
                    assigned = True
                    break

        # Export each material group
        for material, objects in material_groups.items():
            # Select objects for export
            bpy.ops.object.select_all(action="DESELECT")
            for obj in objects:
                obj.select_set(True)
            
            if objects:
                context.view_layer.objects.active = objects[0]

            # Generate filename
            if material:
                base_name = bpy.path.clean_name(material.name)
                # Extract BlenRose material data if enabled
                if hasattr(material, "blenrose_settings") and material.blenrose_settings.enabled:
                    mat_data = _extract_blenrose_material_data(material)
                    # Add assigned splines to this material
                    assigned_splines = material_splines.get(material, [])
                    mat_data["splines"] = [
                        {
                            "name": s["name"],
                            "points": s["points"],
                            "is_closed": s["is_closed"],
                            "type": s["type"],
                            "bbox": s["bbox"],
                        }
                        for s in assigned_splines
                    ]
                    exported_materials[material.name] = mat_data
            else:
                base_name = "NO_MATERIAL"

            # Make filename unique
            safe_name = base_name
            counter = 1
            glb_path = os.path.join(export_dir, f"{safe_name}.glb")
            while os.path.exists(glb_path):
                safe_name = f"{base_name}_{counter}"
                glb_path = os.path.join(export_dir, f"{safe_name}.glb")
                counter += 1

            # Export GLB (tangents disabled: Blender can produce malformed tangents for some meshes;
            # PsgBuilder computes its own tangents from positions/normals/UVs)
            try:
                bpy.ops.export_scene.gltf(
                    filepath=glb_path,
                    export_format="GLB",
                    use_selection=True,
                    export_yup=True,
                    export_apply=True,
                    export_normals=True,
                    export_tangents=False,
                    export_texcoords=True,
                    export_materials="EXPORT",
                )
                exported_objects += len(objects)
            except Exception as e:
                failed += 1
                self.report({"ERROR"}, f"Failed to export {base_name}: {e}")

        # Export material data to JSON
        if exported_materials:
            json_path = os.path.join(export_dir, "blenrose_materials.json")
            try:
                with open(json_path, "w") as f:
                    json.dump(exported_materials, f, indent=2)
                self.report(
                    {"INFO"},
                    f"Exported {len(exported_materials)} BlenRose material(s) to {json_path}",
                )
            except Exception as e:
                self.report({"ERROR"}, f"Failed to export material data: {e}")

        self.report(
            {"INFO"},
            f"✅ Exported {exported_objects} object(s), {len(exported_materials)} material(s). {failed} failed.",
        )
        return {"FINISHED"}

    def invoke(self, context, event):
        context.window_manager.fileselect_add(self)
        return {"RUNNING_MODAL"}


# -------------------------------------------------------------------------
# Simple entry in Material Properties to open the editor
# -------------------------------------------------------------------------

class BLENROSE_PT_material_panel(Panel):
    bl_label = "Blenrose Editor"
    bl_idname = "BLENROSE_PT_material_panel"
    bl_space_type = "PROPERTIES"
    bl_region_type = "WINDOW"
    bl_context = "material"

    def draw(self, context):
        layout = self.layout
        layout.operator("blenrose.material_editor", icon="MATERIAL")
        layout.operator("blenrose.enable_all_and_prepare_export", icon="SHADERFX", text="Enable All & Prepare for Export")


# -------------------------------------------------------------------------
# Scene‑level state (active material index for UIList)
# -------------------------------------------------------------------------

def _scene_props():
    bpy.types.Scene.blenrose_mat_index = IntProperty(
        name="Blenrose Material Index", default=0
    )


# -------------------------------------------------------------------------
# Registration
# -------------------------------------------------------------------------

classes = (
    BlenroseMaterialSettings,
    BLENROSE_UL_materials,
    BLENROSE_OT_new_material,
    BLENROSE_OT_delete_material,
    BLENROSE_OT_auto_detect_textures,
    BLENROSE_OT_enable_all_and_prepare_export,
    BLENROSE_OT_bulk_export,
    BLENROSE_OT_material_editor,
    BLENROSE_PT_material_panel,
)


def _node_tree_update_handler(scene):
    """
    Handler that auto-detects textures when node trees are updated.
    Called when materials are modified in the shader editor.
    This syncs BlenRose UI with changes in the shader node tree.
    """
    # Only process materials that have Blenrose enabled
    # Use a flag to prevent infinite loops
    if not hasattr(bpy.app, "_blenrose_updating"):
        bpy.app._blenrose_updating = False
    
    if bpy.app._blenrose_updating:
        return
    
    try:
        bpy.app._blenrose_updating = True
        
        for mat in bpy.data.materials:
            if not mat or not mat.use_nodes or not mat.node_tree:
                continue
            
            settings = getattr(mat, "blenrose_settings", None)
            if not settings or not settings.enabled:
                continue
            
            # Auto-detect and update textures (sync with node tree changes)
            # Only update if the detected texture is different from current
            detected = _detect_textures_from_node_tree(mat)
            
            texture_prop_map = {
                "diffuse": "diffuse_tex",
                "lightmap": "lightmap_tex",
                "specular": "specular_tex",
                "normal": "normal_tex",
                "detail": "detail_tex",
                "macro_overlay": "macro_overlay_tex",
                "environment": "environment_tex",
                "decal": "decal_tex",
                "transparent": "transparent_tex",
                "noise": "noise_tex",
            }
            
            for channel_name, texture_info in detected.items():
                tex_prop = texture_prop_map.get(channel_name)
                if tex_prop:
                    current_image = getattr(settings, tex_prop, None)
                    detected_image = texture_info.get("image")
                    
                    # Update if different (syncs with node tree)
                    if detected_image and detected_image != current_image:
                        setattr(settings, tex_prop, detected_image)
    finally:
        bpy.app._blenrose_updating = False


def register():
    from bpy.utils import register_class

    # Load material class/subclass tables BEFORE registering classes
    # so EnumProperty default values are valid at registration time
    _load_material_classes_from_xml()

    for cls in classes:
        register_class(cls)

    bpy.types.Material.blenrose_settings = PointerProperty(
        type=BlenroseMaterialSettings
    )

    _scene_props()
    
    # Register node tree update handler to auto-detect textures
    if _node_tree_update_handler not in bpy.app.handlers.depsgraph_update_post:
        bpy.app.handlers.depsgraph_update_post.append(_node_tree_update_handler)


def unregister():
    from bpy.utils import unregister_class

    # Unregister node tree update handler
    if _node_tree_update_handler in bpy.app.handlers.depsgraph_update_post:
        bpy.app.handlers.depsgraph_update_post.remove(_node_tree_update_handler)

    del bpy.types.Material.blenrose_settings
    del bpy.types.Scene.blenrose_mat_index

    for cls in reversed(classes):
        unregister_class(cls)


if __name__ == "__main__":
    register()
