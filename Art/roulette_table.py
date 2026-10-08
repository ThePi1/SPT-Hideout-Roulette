"""Generates the roulette table model: table, betting layout, wheel, ball, chips and win marker.

Run with Blender 3.x:
    blender -b --python Art/roulette_table.py -- --fbx Unity/Assets/Roulette/RouletteTable.fbx --render Art/preview/table

Metres, Z up, origin on the floor under the middle of the table. The player stands on the -Y side with +X to their right.
Everything is a direct child of the root, the FBX export only converts axes cleanly one level down.

    RouletteTable        root
      Table              legs, apron, padded rail and felt
      Layout             printed betting layout lying on the felt
      Bowl               static part of the wheel
      Rotor              spinning part of the wheel, its origin is on the wheel axis
      Ball
      Dolly              win marker, origin at its base
      Chip_<tier>        one chip per denomination tier, origin at its base, copied into stacks by the plugin
    Markers (empties) the plugin reads instead of hard coding sizes:
      TableMin/TableMax          opposite corners of the tables bounding box
      LayoutOrigin/LayoutFarCorner   near left and far right corners of the betting grid, on the felt
      WheelCentre                wheel axis at felt height
      BallTrack                  a point on the circle the ball runs round before it drops
      Pocket_<n>                 where the ball rests for number n, with the rotor at its starting angle
"""

import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

WHEEL_ORDER = [
    0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26,
]
RED_NUMBERS = {1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36}
POCKET_COUNT = len(WHEEL_ORDER)
POCKET_ANGLE = 2 * math.pi / POCKET_COUNT

SEGMENTS = 148  # lathe steps, a multiple of the pocket count so pocket edges land on vertices
ARC_STEPS = SEGMENTS // POCKET_COUNT

# Table
FELT_Z = 0.95
PLAY_MIN_X, PLAY_MAX_X = -1.03, 1.12
PLAY_HALF_DEPTH = 0.39
CORNER_RADIUS = 0.1
RAIL_WIDTH = 0.06
RAIL_HEIGHT = 0.03
APRON_DEPTH = 0.14
LEG_POSITIONS = [(-0.85, -0.29), (-0.85, 0.29), (0.94, -0.29), (0.94, 0.29)]

# Betting layout, a grid of square cells. Must match RouletteLayout in the plugin: 14 columns by 5 rows.
CELL = 0.09
GRID_COLUMNS, GRID_ROWS = 14, 5
LAYOUT_MIN_X, LAYOUT_MIN_Y = -0.2, -0.36
LINE_WIDTH = 0.003
PRINT_LIFT = 0.0005  # each printed layer sits this much above the one below

# Wheel, radii and heights relative to the wheel axis at felt height
WHEEL_X = -0.62
ROTOR_RADIUS = 0.27
NUMBER_RING_INNER = 0.215
POCKET_RING_INNER = 0.165
NUMBER_RING_OUTER_Z = 0.054
NUMBER_RING_INNER_Z = 0.05
POCKET_FLOOR_Z = 0.04
SEPARATOR_HEIGHT = 0.012
CONE_TOP_RADIUS = 0.05
CONE_TOP_Z = 0.078
BALL_RADIUS = 0.011
TRACK_RADIUS = 0.292
TRACK_Z = 0.057

CHIP_RADIUS = 0.021
CHIP_THICKNESS = 0.005
CHIP_SEGMENTS = 24

# name: (base colour, metallic, roughness)
MATERIALS = {
    "Wood": ((0.16, 0.06, 0.025), 0.0, 0.78),
    "WoodLight": ((0.42, 0.2, 0.08), 0.0, 0.72),
    "Brass": ((0.83, 0.62, 0.25), 1.0, 0.3),
    "Leather": ((0.045, 0.02, 0.012), 0.0, 0.68),
    "Felt": ((0.015, 0.19, 0.07), 0.0, 0.95),
    "LayoutPrint": ((0.85, 0.78, 0.55), 0.0, 0.85),
    "LayoutRed": ((0.5, 0.03, 0.03), 0.0, 0.9),
    "LayoutBlack": ((0.012, 0.012, 0.012), 0.0, 0.9),
    "LayoutGreen": ((0.03, 0.36, 0.12), 0.0, 0.9),
    "PocketRed": ((0.55, 0.03, 0.03), 0.0, 0.45),
    "PocketBlack": ((0.02, 0.02, 0.02), 0.0, 0.45),
    "PocketGreen": ((0.02, 0.33, 0.1), 0.0, 0.45),
    "Numbers": ((0.92, 0.9, 0.82), 0.0, 0.5),
    "Ball": ((0.93, 0.91, 0.84), 0.0, 0.2),
    "ChipWhite": ((0.88, 0.87, 0.82), 0.0, 0.4),
    "ChipRed": ((0.6, 0.04, 0.04), 0.0, 0.4),
    "ChipGreen": ((0.03, 0.4, 0.12), 0.0, 0.4),
    "ChipBlack": ((0.02, 0.02, 0.02), 0.0, 0.4),
    "ChipInlayBlue": ((0.05, 0.15, 0.55), 0.0, 0.4),
    "ChipInlayCream": ((0.9, 0.85, 0.7), 0.0, 0.4),
}

# Body and inlay material per denomination tier, lowest first
CHIP_TIERS = [("ChipWhite", "ChipInlayBlue"), ("ChipRed", "ChipInlayCream"), ("ChipGreen", "ChipInlayCream"), ("ChipBlack", "Brass")]


# ---------------------------------------------------------------------------------------------------------------------
# Mesh helpers


def polar(radius, angle, z, centre=(0.0, 0.0)):
    """Angles run clockwise from +Y seen from above."""
    return (centre[0] + radius * math.sin(angle), centre[1] + radius * math.cos(angle), z)


def get_material(name):
    material = bpy.data.materials.get(name)
    if material is None:
        color, metallic, roughness = MATERIALS[name]
        material = bpy.data.materials.new(name)
        material.use_nodes = True
        shader = material.node_tree.nodes["Principled BSDF"]
        shader.inputs["Base Color"].default_value = (*color, 1.0)
        shader.inputs["Metallic"].default_value = metallic
        shader.inputs["Roughness"].default_value = roughness
        material.diffuse_color = (*color, 1.0)
    return material


def finish_mesh(name, bm, materials, smooth_angle=40, flat_print=False):
    """Turn a bmesh into an object. Each face's material_index refers to the materials list."""
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    if flat_print:
        # Loose flat faces give the normal recalculation nothing to go on, and game engines hide faces that point away
        bm.normal_update()
        for face in bm.faces:
            if face.normal.z < 0:
                face.normal_flip()
    else:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # Texture coordinates are in metres. Faces that were not unwrapped by their builder, and any that face up or down,
    # take them straight from position seen from above, with the grain along the length of the table. A flat piece of
    # wood shows its grain that way however it was turned.
    uv_layer, unwrapped = uv_layers(bm)
    bm.normal_update()
    for face in bm.faces:
        face.smooth = True
        if not face[unwrapped] or abs(face.normal.z) > 0.7:
            for loop in face.loops:
                loop[uv_layer].uv = (loop.vert.co.y, loop.vert.co.x)

    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for material in materials:
        mesh.materials.append(get_material(material))
    mesh.use_auto_smooth = True
    mesh.auto_smooth_angle = math.radians(smooth_angle)

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def uv_layers(bm):
    """Texture coordinate layer, and a per face flag saying the face was unwrapped by whatever built it."""
    flag = bm.faces.layers.int.get("unwrapped") or bm.faces.layers.int.new("unwrapped")
    return bm.loops.layers.uv.verify(), flag


def set_face_uvs(bm, face, coordinates):
    """Texture coordinates in metres for each corner of a face, in the order its vertices were given."""
    uv_layer, flag = uv_layers(bm)
    for loop, coordinate in zip(face.loops, coordinates):
        loop[uv_layer].uv = coordinate
    face[flag] = 1


def running_length(points):
    """Distance along a line of points up to each one."""
    lengths = [0.0]
    for (a0, b0), (a1, b1) in zip(points, points[1:]):
        lengths.append(lengths[-1] + math.hypot(a1 - a0, b1 - b0))
    return lengths


def add_lathe(bm, profile, material_index=0, segments=SEGMENTS, centre=(0.0, 0.0), grain_around=False):
    """Revolve a list of (radius, z) points round a vertical axis. Points on the axis become a single vertex.

    Unwrapped like a label round a tin. Textures run their grain along V, which is up the shape unless grain_around.
    """
    # Adding a data layer invalidates faces already held, so the layers have to exist before any are made
    uv_layers(bm)

    rings = []
    for radius, z in profile:
        if radius < 1e-6:
            rings.append([bm.verts.new((centre[0], centre[1], z))])
        else:
            rings.append([bm.verts.new(polar(radius, 2 * math.pi * i / segments, z, centre)) for i in range(segments)])

    along = running_length(profile)
    girth = 2 * math.pi * max(radius for radius, _ in profile)

    def uv(ring, step):
        around, up = girth * step / segments, along[ring]
        return (up, around) if grain_around else (around, up)

    for ring, (lower, upper) in enumerate(zip(rings, rings[1:])):
        for i in range(segments):
            j = (i + 1) % segments
            if len(lower) == 1 and len(upper) == 1:
                continue
            if len(lower) == 1:
                verts, uvs = (lower[0], upper[i], upper[j]), (uv(ring, i), uv(ring + 1, i), uv(ring + 1, i + 1))
            elif len(upper) == 1:
                verts, uvs = (lower[i], lower[j], upper[0]), (uv(ring, i), uv(ring, i + 1), uv(ring + 1, i))
            else:
                verts = (lower[i], lower[j], upper[j], upper[i])
                uvs = (uv(ring, i), uv(ring, i + 1), uv(ring + 1, i + 1), uv(ring + 1, i))
            face = bm.faces.new(verts)
            face.material_index = material_index
            set_face_uvs(bm, face, uvs)


def add_box(bm, corners_bottom, height, material_index=0):
    """Extrude a four cornered footprint straight up."""
    bottom = [bm.verts.new(corner) for corner in corners_bottom]
    top = [bm.verts.new((x, y, z + height)) for x, y, z in corners_bottom]
    bm.faces.new(bottom).material_index = material_index
    bm.faces.new(top).material_index = material_index
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((bottom[i], bottom[j], top[j], top[i])).material_index = material_index


def add_octahedron(bm, centre, half_sizes, angle, material_index=0):
    """Diamond shaped stud, half_sizes are (tangential, radial, vertical) before turning by angle."""
    cx, cy, cz = centre
    cos_a, sin_a = math.cos(angle), math.sin(angle)

    def place(tangential, radial, vertical):
        # Tangential runs clockwise, radial outwards
        x = tangential * cos_a + radial * sin_a
        y = -tangential * sin_a + radial * cos_a
        return bm.verts.new((cx + x, cy + y, cz + vertical))

    st, sr, sv = half_sizes
    top, bottom = place(0, 0, sv), place(0, 0, -sv)
    ring = [place(st, 0, 0), place(0, sr, 0), place(-st, 0, 0), place(0, -sr, 0)]
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((ring[i], ring[j], top)).material_index = material_index
        bm.faces.new((ring[j], ring[i], bottom)).material_index = material_index


def add_sphere(bm, centre, radius, material_index=0):
    result = bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=6, radius=radius)
    for vert in result["verts"]:
        vert.co += Vector(centre)
        for face in vert.link_faces:
            face.material_index = material_index


def add_rect(bm, x0, y0, x1, y1, z, material_index=0):
    """Flat rectangle facing up."""
    bm.faces.new([bm.verts.new(p) for p in ((x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z))]).material_index = material_index


def rounded_rect_path(min_x, max_x, min_y, max_y, radius, corner_steps=8):
    """Outline of a rounded rectangle, anticlockwise, as (x, y, outward normal x, outward normal y)."""
    centres = [(max_x - radius, max_y - radius), (min_x + radius, max_y - radius), (min_x + radius, min_y + radius), (max_x - radius, min_y + radius)]
    path = []
    for corner, (cx, cy) in enumerate(centres):
        for step in range(corner_steps + 1):
            angle = (corner + step / corner_steps) * math.pi / 2
            nx, ny = math.cos(angle), math.sin(angle)
            path.append((cx + radius * nx, cy + radius * ny, nx, ny))
    return path


def add_sweep(bm, path, profile, material_index=0):
    """Run a profile of (outward offset, z) points round a closed path. Unwrapped with the grain running along the path."""
    uv_layers(bm)
    rings = [[bm.verts.new((x + nx * offset, y + ny * offset, z)) for offset, z in profile] for x, y, nx, ny in path]
    across = running_length(profile)
    closed_path = [(x, y) for x, y, _, _ in path] + [path[0][:2]]
    along = running_length(closed_path)
    for index, ring in enumerate(rings):
        following = rings[(index + 1) % len(rings)]
        for k in range(len(profile) - 1):
            face = bm.faces.new((ring[k], following[k], following[k + 1], ring[k + 1]))
            face.material_index = material_index
            set_face_uvs(
                bm, face, ((across[k], along[index]), (across[k], along[index + 1]), (across[k + 1], along[index + 1]), (across[k + 1], along[index]))
            )


def add_path_cap(bm, path, offset, z, material_index=0):
    """Flat face filling a closed path."""
    bm.faces.new([bm.verts.new((x + nx * offset, y + ny * offset, z)) for x, y, nx, ny in path]).material_index = material_index


def make_text(body, size, location, rotation=(0.0, 0.0, 0.0), squeeze=1.0):
    """Centred text object, flat in its own XY plane."""
    curve = bpy.data.curves.new("Text", "FONT")
    curve.body = body
    curve.align_x = "CENTER"
    curve.align_y = "CENTER"
    curve.size = size
    curve.resolution_u = 3
    obj = bpy.data.objects.new("Text", curve)
    obj.scale = (squeeze, 1.0, 1.0)
    obj.rotation_euler = rotation
    obj.location = location
    bpy.context.scene.collection.objects.link(obj)
    return obj


def join_into(target, others):
    """Merge objects into target. Text is converted to mesh first, it does not survive export otherwise."""
    bpy.ops.object.select_all(action="DESELECT")
    for obj in others:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = others[0]
    bpy.ops.object.convert(target="MESH")
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.join()
    return target


def set_text_material(objects, material):
    for obj in objects:
        obj.data.materials.append(get_material(material))


def make_marker(name, location):
    marker = bpy.data.objects.new(name, None)
    marker.empty_display_size = 0.01
    marker.location = location
    bpy.context.scene.collection.objects.link(marker)
    return marker


# ---------------------------------------------------------------------------------------------------------------------
# Table and layout


def build_table():
    bm = bmesh.new()
    play_edge = rounded_rect_path(PLAY_MIN_X, PLAY_MAX_X, -PLAY_HALF_DEPTH, PLAY_HALF_DEPTH, CORNER_RADIUS)
    apron_bottom = FELT_Z - APRON_DEPTH

    # Materials: 0 wood, 1 light wood, 2 brass, 3 leather, 4 felt
    add_path_cap(bm, play_edge, 0.0, FELT_Z, material_index=4)

    # Padded rail round the playing surface
    add_sweep(
        bm,
        play_edge,
        [
            (0.0, FELT_Z),
            (0.0, FELT_Z + RAIL_HEIGHT * 0.6),
            (0.008, FELT_Z + RAIL_HEIGHT * 0.9),
            (0.02, FELT_Z + RAIL_HEIGHT),
            (RAIL_WIDTH - 0.02, FELT_Z + RAIL_HEIGHT),
            (RAIL_WIDTH - 0.006, FELT_Z + RAIL_HEIGHT * 0.8),
            (RAIL_WIDTH, FELT_Z + RAIL_HEIGHT * 0.4),
            (RAIL_WIDTH, FELT_Z - 0.012),
        ],
        material_index=3,
    )

    # Brass strip under the rail, then the wooden apron
    add_sweep(bm, play_edge, [(RAIL_WIDTH, FELT_Z - 0.012), (RAIL_WIDTH + 0.003, FELT_Z - 0.014), (RAIL_WIDTH + 0.003, FELT_Z - 0.022), (RAIL_WIDTH, FELT_Z - 0.024)], material_index=2)
    add_sweep(
        bm,
        play_edge,
        [
            (RAIL_WIDTH, FELT_Z - 0.024),
            (RAIL_WIDTH - 0.004, FELT_Z - 0.03),
            (RAIL_WIDTH - 0.004, apron_bottom + 0.02),
            (RAIL_WIDTH - 0.012, apron_bottom),
            (RAIL_WIDTH - 0.04, apron_bottom),
        ],
        material_index=0,
    )
    add_path_cap(bm, play_edge, RAIL_WIDTH - 0.04, apron_bottom, material_index=0)

    # Turned legs on brass feet
    for centre in LEG_POSITIONS:
        add_lathe(bm, [(0.0, 0.0), (0.048, 0.0), (0.056, 0.008), (0.056, 0.035), (0.042, 0.045)], material_index=2, segments=20, centre=centre)
        add_lathe(
            bm,
            [
                (0.042, 0.045),
                (0.037, 0.09),
                (0.042, 0.3),
                (0.06, 0.42),
                (0.07, 0.5),
                (0.06, 0.56),
                (0.045, 0.6),
                (0.05, 0.7),
                (0.068, 0.74),
                (0.072, apron_bottom),
            ],
            material_index=1,
            segments=20,
            centre=centre,
        )

    return finish_mesh("Table", bm, ["Wood", "WoodLight", "Brass", "Leather", "Felt"])


def layout_spots():
    """(column, row, column span, row span, fill material index or None, text, text size). Row 0 is nearest the player."""
    spots = [(0, 2, 1, 3, 2, "0", 0.045)]
    for number in range(1, 37):
        fill = 0 if number in RED_NUMBERS else 1
        spots.append((1 + (number - 1) // 3, 2 + (number - 1) % 3, 1, 1, fill, str(number), 0.04))
    for i in range(3):
        spots.append((13, 2 + i, 1, 1, None, "2 to 1", 0.024))
        spots.append((1 + i * 4, 1, 4, 1, None, ("1st 12", "2nd 12", "3rd 12")[i], 0.04))
    for i, text in enumerate(("1 to 18", "EVEN", None, None, "ODD", "19 to 36")):
        spots.append((1 + i * 2, 0, 2, 1, None, text, 0.034))
    return spots


def build_layout():
    bm = bmesh.new()
    fill_z = FELT_Z + PRINT_LIFT
    line_z = FELT_Z + PRINT_LIFT * 2
    text_z = FELT_Z + PRINT_LIFT * 3
    half_line = LINE_WIDTH / 2
    texts = []

    # Materials: 0 red, 1 black, 2 green, 3 print
    for column, row, column_span, row_span, fill, text, text_size in layout_spots():
        x0 = LAYOUT_MIN_X + column * CELL
        y0 = LAYOUT_MIN_Y + row * CELL
        x1 = x0 + column_span * CELL
        y1 = y0 + row_span * CELL

        if fill is not None:
            add_rect(bm, x0, y0, x1, y1, fill_z, material_index=fill)

        # Border, shared edges are simply drawn twice
        add_rect(bm, x0 - half_line, y0 - half_line, x1 + half_line, y0 + half_line, line_z, material_index=3)
        add_rect(bm, x0 - half_line, y1 - half_line, x1 + half_line, y1 + half_line, line_z, material_index=3)
        add_rect(bm, x0 - half_line, y0 - half_line, x0 + half_line, y1 + half_line, line_z, material_index=3)
        add_rect(bm, x1 - half_line, y0 - half_line, x1 + half_line, y1 + half_line, line_z, material_index=3)

        centre_x, centre_y = (x0 + x1) / 2, (y0 + y1) / 2
        if text is not None:
            texts.append(make_text(text, text_size, (centre_x, centre_y, text_z), squeeze=0.8))

    # Red and black are marked with a diamond rather than a word
    for index, material_index in ((2, 0), (3, 1)):
        centre_x = LAYOUT_MIN_X + (1 + index * 2 + 1) * CELL
        centre_y = LAYOUT_MIN_Y + CELL / 2
        half_w, half_h = CELL * 0.6, CELL * 0.3
        bm.faces.new(
            [bm.verts.new(p) for p in ((centre_x - half_w, centre_y, fill_z), (centre_x, centre_y - half_h, fill_z), (centre_x + half_w, centre_y, fill_z), (centre_x, centre_y + half_h, fill_z))]
        ).material_index = material_index

    layout = finish_mesh("Layout", bm, ["LayoutRed", "LayoutBlack", "LayoutGreen", "LayoutPrint"], flat_print=True)
    set_text_material(texts, "LayoutPrint")
    return join_into(layout, texts)


# ---------------------------------------------------------------------------------------------------------------------
# Wheel, built round the origin then moved onto the table


def build_bowl():
    bm = bmesh.new()

    # Outside wall, rim, then the sloped track the ball runs on, down to the floor under the rotor
    add_lathe(bm, [(0.0, 0.0), (0.355, 0.0), (0.365, 0.012), (0.365, 0.086), (0.358, 0.097), (0.338, 0.097), (0.328, 0.084)], material_index=0, grain_around=True)
    add_lathe(bm, [(0.328, 0.084), (0.31, 0.066), (0.276, 0.05), (0.274, 0.03), (0.0, 0.03)], material_index=1)

    # Brass band round the top of the rim
    add_lathe(bm, [(0.359, 0.097), (0.357, 0.1005), (0.339, 0.1005), (0.337, 0.097)], material_index=2)

    # Deflectors that knock the ball about on its way down, alternately along and across the track
    for i in range(8):
        angle = 2 * math.pi * (i + 0.5) / 8
        along_track = i % 2 == 0
        half_sizes = (0.02, 0.0045, 0.004) if along_track else (0.0045, 0.014, 0.004)
        add_octahedron(bm, polar(0.3, angle, 0.0635), half_sizes, angle, material_index=2)

    return finish_mesh("Bowl", bm, ["Wood", "WoodLight", "Brass"])


def build_rotor():
    bm = bmesh.new()

    # Materials: 0 red, 1 black, 2 green, 3 brass, 4 light wood
    for slot, number in enumerate(WHEEL_ORDER):
        colour = 2 if number == 0 else (0 if number in RED_NUMBERS else 1)
        start = (slot - 0.5) * POCKET_ANGLE

        # Number band and pocket floor, as arcs so they follow the circle
        for step in range(ARC_STEPS):
            a0 = start + POCKET_ANGLE * step / ARC_STEPS
            a1 = start + POCKET_ANGLE * (step + 1) / ARC_STEPS
            bm.faces.new(
                [
                    bm.verts.new(polar(NUMBER_RING_INNER, a0, NUMBER_RING_INNER_Z)),
                    bm.verts.new(polar(ROTOR_RADIUS, a0, NUMBER_RING_OUTER_Z)),
                    bm.verts.new(polar(ROTOR_RADIUS, a1, NUMBER_RING_OUTER_Z)),
                    bm.verts.new(polar(NUMBER_RING_INNER, a1, NUMBER_RING_INNER_Z)),
                ]
            ).material_index = colour
            bm.faces.new(
                [
                    bm.verts.new(polar(POCKET_RING_INNER, a0, POCKET_FLOOR_Z)),
                    bm.verts.new(polar(NUMBER_RING_INNER, a0, POCKET_FLOOR_Z)),
                    bm.verts.new(polar(NUMBER_RING_INNER, a1, POCKET_FLOOR_Z)),
                    bm.verts.new(polar(POCKET_RING_INNER, a1, POCKET_FLOOR_Z)),
                ]
            ).material_index = colour

        # Brass wall between this pocket and the previous one
        half_width = 0.0012 / ((POCKET_RING_INNER + NUMBER_RING_INNER) / 2)
        add_box(
            bm,
            [
                polar(POCKET_RING_INNER, start - half_width, POCKET_FLOOR_Z),
                polar(NUMBER_RING_INNER, start - half_width, POCKET_FLOOR_Z),
                polar(NUMBER_RING_INNER, start + half_width, POCKET_FLOOR_Z),
                polar(POCKET_RING_INNER, start + half_width, POCKET_FLOOR_Z),
            ],
            SEPARATOR_HEIGHT,
            material_index=3,
        )

    # Outer edge of the rotor, brass
    add_lathe(bm, [(ROTOR_RADIUS - 0.002, 0.032), (ROTOR_RADIUS, 0.034), (ROTOR_RADIUS, NUMBER_RING_OUTER_Z)], material_index=3)

    # Step down from the number band into the pockets, and the ring on the inside of the pockets
    add_lathe(bm, [(NUMBER_RING_INNER, NUMBER_RING_INNER_Z), (NUMBER_RING_INNER, POCKET_FLOOR_Z)], material_index=3)
    add_lathe(
        bm,
        [(POCKET_RING_INNER, POCKET_FLOOR_Z), (POCKET_RING_INNER, POCKET_FLOOR_Z + SEPARATOR_HEIGHT), (POCKET_RING_INNER - 0.004, POCKET_FLOOR_Z + SEPARATOR_HEIGHT)],
        material_index=3,
    )

    # Polished cone rising to the turret
    add_lathe(bm, [(POCKET_RING_INNER - 0.004, POCKET_FLOOR_Z + SEPARATOR_HEIGHT), (0.1, 0.066), (CONE_TOP_RADIUS, CONE_TOP_Z)], material_index=4)

    # Turret: collar, waisted column, cap and finial
    add_lathe(
        bm,
        [
            (CONE_TOP_RADIUS, CONE_TOP_Z),
            (CONE_TOP_RADIUS, 0.084),
            (0.04, 0.089),
            (0.022, 0.095),
            (0.014, 0.112),
            (0.014, 0.128),
            (0.024, 0.134),
            (0.026, 0.14),
            (0.018, 0.146),
            (0.008, 0.149),
            (0.006, 0.156),
            (0.011, 0.162),
            (0.011, 0.168),
            (0.0, 0.174),
        ],
        material_index=3,
        segments=37,
    )

    # Four spokes with round knobs, the handles a croupier spins the wheel by
    for i in range(4):
        angle = i * math.pi / 2
        half_width = 0.0035
        inner, outer, z = 0.012, 0.062, 0.119
        dx, dy = half_width * math.cos(angle), -half_width * math.sin(angle)
        x0, y0, _ = polar(inner, angle, z)
        x1, y1, _ = polar(outer, angle, z)
        add_box(bm, [(x0 - dx, y0 - dy, z), (x1 - dx, y1 - dy, z), (x1 + dx, y1 + dy, z), (x0 + dx, y0 + dy, z)], 0.007, material_index=3)
        add_sphere(bm, polar(outer + 0.003, angle, z + 0.0035), 0.008, material_index=3)

    rotor = finish_mesh("Rotor", bm, ["PocketRed", "PocketBlack", "PocketGreen", "Brass", "WoodLight"])

    # Flat lettering lying on the number band, tops towards the rim
    radius = (NUMBER_RING_INNER + ROTOR_RADIUS) / 2
    slope = math.atan2(NUMBER_RING_OUTER_Z - NUMBER_RING_INNER_Z, ROTOR_RADIUS - NUMBER_RING_INNER)
    height = (NUMBER_RING_INNER_Z + NUMBER_RING_OUTER_Z) / 2 + 0.0006
    numbers = [
        # Squeezed so two digits fit across a pocket. Tilted to follow the band, then turned so the glyphs up axis points outwards.
        make_text(str(number), 0.03, polar(radius, slot * POCKET_ANGLE, height), rotation=(slope, 0.0, -slot * POCKET_ANGLE), squeeze=0.72)
        for slot, number in enumerate(WHEEL_ORDER)
    ]
    set_text_material(numbers, "Numbers")
    return join_into(rotor, numbers)


def build_ball():
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=BALL_RADIUS)
    return finish_mesh("Ball", bm, ["Ball"], smooth_angle=180)


# ---------------------------------------------------------------------------------------------------------------------
# Chips and win marker


def build_chip(tier, body_material, inlay_material):
    """One chip with its origin at the centre of its base. Six inlays run over the edge."""
    bm = bmesh.new()
    bevel = 0.0008
    profile = [
        (0.0, 0.0),
        (CHIP_RADIUS * 0.62, 0.0),
        (CHIP_RADIUS - bevel, 0.0),
        (CHIP_RADIUS, bevel),
        (CHIP_RADIUS, CHIP_THICKNESS - bevel),
        (CHIP_RADIUS - bevel, CHIP_THICKNESS),
        (CHIP_RADIUS * 0.62, CHIP_THICKNESS),
        (0.0, CHIP_THICKNESS),
    ]
    rings = [
        [bm.verts.new((0, 0, z))] if radius < 1e-6 else [bm.verts.new(polar(radius, 2 * math.pi * i / CHIP_SEGMENTS, z)) for i in range(CHIP_SEGMENTS)]
        for radius, z in profile
    ]
    for ring_index, (lower, upper) in enumerate(zip(rings, rings[1:])):
        # Everything outside the centre disc belongs to the edge band
        on_edge = 1 <= ring_index <= 5
        for i in range(CHIP_SEGMENTS):
            j = (i + 1) % CHIP_SEGMENTS
            if len(lower) == 1:
                verts = (lower[0], upper[i], upper[j])
            elif len(upper) == 1:
                verts = (lower[i], lower[j], upper[0])
            else:
                verts = (lower[i], lower[j], upper[j], upper[i])
            inlay = on_edge and (i // 2) % 2 == 0
            bm.faces.new(verts).material_index = 1 if inlay else 0

    return finish_mesh(f"Chip_{tier}", bm, [body_material, inlay_material], smooth_angle=30)


def build_dolly():
    bm = bmesh.new()
    add_lathe(
        bm,
        [(0.0, 0.0), (0.016, 0.0), (0.017, 0.002), (0.017, 0.006), (0.009, 0.012), (0.006, 0.03), (0.008, 0.036), (0.011, 0.04), (0.011, 0.045), (0.007, 0.05), (0.0, 0.052)],
        segments=24,
    )
    return finish_mesh("Dolly", bm, ["Brass"])


# ---------------------------------------------------------------------------------------------------------------------
# Scene, export and previews


def build_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)

    root = bpy.data.objects.new("RouletteTable", None)
    bpy.context.scene.collection.objects.link(root)

    wheel_origin = Vector((WHEEL_X, 0.0, FELT_Z))
    pocket_radius = (POCKET_RING_INNER + NUMBER_RING_INNER) / 2

    bowl, rotor, ball = build_bowl(), build_rotor(), build_ball()
    bowl.location = wheel_origin
    rotor.location = wheel_origin
    ball.location = wheel_origin + Vector(polar(pocket_radius, 0.0, POCKET_FLOOR_Z + BALL_RADIUS))

    children = [build_table(), build_layout(), bowl, rotor, ball]

    # Parked on the felt beyond the layout, the plugin only uses these as templates
    dolly = build_dolly()
    dolly.location = (0.75, 0.24, FELT_Z)
    children.append(dolly)
    for tier, (body, inlay) in enumerate(CHIP_TIERS):
        chip = build_chip(tier, body, inlay)
        chip.location = (0.2 + tier * 0.07, 0.24, FELT_Z)
        children.append(chip)

    children += [
        make_marker("TableMin", (PLAY_MIN_X - RAIL_WIDTH, -PLAY_HALF_DEPTH - RAIL_WIDTH, 0.0)),
        make_marker("TableMax", (PLAY_MAX_X + RAIL_WIDTH, PLAY_HALF_DEPTH + RAIL_WIDTH, FELT_Z + RAIL_HEIGHT)),
        make_marker("LayoutOrigin", (LAYOUT_MIN_X, LAYOUT_MIN_Y, FELT_Z)),
        make_marker("LayoutFarCorner", (LAYOUT_MIN_X + GRID_COLUMNS * CELL, LAYOUT_MIN_Y + GRID_ROWS * CELL, FELT_Z)),
        make_marker("WheelCentre", wheel_origin),
        make_marker("BallTrack", wheel_origin + Vector(polar(TRACK_RADIUS, 0.0, TRACK_Z + BALL_RADIUS))),
    ]
    for slot, number in enumerate(WHEEL_ORDER):
        children.append(make_marker(f"Pocket_{number}", wheel_origin + Vector(polar(pocket_radius, slot * POCKET_ANGLE, POCKET_FLOOR_Z + BALL_RADIUS))))

    for child in children:
        child.parent = root


def export_fbx(path):
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"MESH", "EMPTY"},
        apply_scale_options="FBX_SCALE_UNITS",
        bake_space_transform=True,
        axis_forward="-Z",
        axis_up="Y",
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        bake_anim=False,
    )

    # FBX carries little more than a colour, the Unity build script makes the real materials from this
    materials = [
        {"name": name, "r": color[0], "g": color[1], "b": color[2], "metallic": metallic, "roughness": roughness}
        for name, (color, metallic, roughness) in MATERIALS.items()
    ]
    with open(os.path.splitext(path)[0] + ".materials.json", "w") as handle:
        json.dump({"materials": materials}, handle, indent=2)


def aim_at(location, target):
    return (Vector(target) - Vector(location)).to_track_quat("-Z", "Y").to_euler()


def render_previews(prefix):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 1400
    scene.render.resolution_y = 900
    scene.view_settings.view_transform = "Standard"

    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.05, 0.05, 0.055, 1.0)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.6
    scene.world = world

    floor_material = bpy.data.materials.new("PreviewFloor")
    floor_material.diffuse_color = (0.12, 0.12, 0.12, 1.0)
    bpy.ops.mesh.primitive_plane_add(size=8, location=(0, 0, 0))
    bpy.context.active_object.data.materials.append(floor_material)

    for location, energy in (((0.8, -1.4, 2.4), 80), ((-1.6, 0.6, 2.0), 35)):
        light_data = bpy.data.lights.new("Light", "AREA")
        light_data.energy = energy
        light_data.size = 1.2
        light = bpy.data.objects.new("Light", light_data)
        light.location = location
        light.rotation_euler = aim_at(location, (0, 0, FELT_Z))
        scene.collection.objects.link(light)

    camera_data = bpy.data.cameras.new("Camera")
    camera_data.lens = 40
    camera = bpy.data.objects.new("Camera", camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera

    views = {
        "whole": ((1.9, -2.6, 1.9), (0.05, 0, 0.6)),
        "player": ((0.05, -1.25, 2.1), (0.05, 0.0, FELT_Z)),
        "layout": ((0.45, -0.75, 1.5), (0.45, -0.1, FELT_Z)),
        "chips": ((0.32, 0.02, 1.08), (0.32, 0.24, FELT_Z)),
    }
    for name, (location, target) in views.items():
        camera.location = location
        camera.rotation_euler = aim_at(location, target)
        scene.render.filepath = f"{prefix}_{name}.png"
        bpy.ops.render.render(write_still=True)


def main():
    args = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    options = dict(zip(args[::2], args[1::2]))

    build_scene()

    triangles = sum(len(p.vertices) - 2 for o in bpy.data.objects if o.type == "MESH" for p in o.data.polygons)
    print(f"TABLE triangles={triangles}")

    if "--fbx" in options:
        export_fbx(options["--fbx"])
        print(f"TABLE exported {options['--fbx']}")
    if "--render" in options:
        render_previews(options["--render"])
        print(f"TABLE rendered {options['--render']}")


main()
