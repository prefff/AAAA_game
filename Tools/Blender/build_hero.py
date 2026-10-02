# Генератор модели бойца «Разрядник» для AAAA_game.
#
# Запуск:
#   - в открытом Blender (Scripting → Run Script) или через аддон Blender MCP;
#   - без интерфейса: blender -b --factory-startup -P Tools/Blender/build_hero.py
# Результат: Assets/_Project/Art/Characters/Hero/Hero.fbx (меш + скелет, без анимаций — анимации процедурные,
# их считает FighterRig в Unity по frame data симуляции).
#
# Устройство модели (дёшево для мобилок):
#   - один меш, один материал, без текстур: цвет — в цвете вершин;
#     альфа вершины — маска: 0 — обычный цвет, 0.5 — цвет команды (капюшон, шарф, пояс), 1 — свечение (перчатки, ядро, глаза);
#   - жёсткая привязка: каждая часть целиком на одной кости (стиль «шарнирной фигурки», суставы — шарами);
#   - ~3 тыс. треугольников, 20 костей.
# Оси: персонаж смотрит в -Y Blender (после FBX в Unity — +Z), правая рука — в -X.

import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

try:
    ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
except NameError:  # exec() из аддона: __file__ нет
    ROOT = r"C:\Users\goddammit\Desktop\game\AAAA_game"
OUT_FBX = os.path.join(ROOT, "Assets", "_Project", "Art", "Characters", "Hero", "Hero.fbx")
OUT_BLEND = os.path.join(ROOT, "Tools", "Blender", "hero.blend")

# ---------- палитра (sRGB) ----------
SUIT = (0.15, 0.16, 0.20)
SUIT_LIGHT = (0.24, 0.25, 0.30)
METAL = (0.36, 0.38, 0.43)
METAL_DARK = (0.22, 0.23, 0.27)
WRAP = (0.80, 0.76, 0.68)
LEATHER = (0.36, 0.25, 0.17)
BOOT = (0.20, 0.17, 0.15)
FACE = (0.05, 0.05, 0.07)
TEAM = (0.92, 0.92, 0.92)   # домножается на цвет команды в шейдере
GLOW = (1.0, 1.0, 1.0)      # излучает цвет свечения

A_BASE, A_TEAM, A_GLOW = 0.0, 0.5, 1.0

# ---------- скелет: имя → (голова, хвост, родитель) в «персонажных» координатах ----------
# P(xr, yf, z): xr — вправо от персонажа, yf — вперёд, z — вверх.


def P(xr, yf, z):
    return Vector((-xr, -yf, z))


SHOULDER_X = 0.29
HIP_X = 0.11

BONES = [
    ("Root", P(0, 0, 0), P(0, 0, 0.25), None),
    ("Hips", P(0, 0, 0.93), P(0, 0, 1.05), "Root"),
    ("Spine", P(0, 0, 1.05), P(0, 0, 1.20), "Hips"),
    ("Chest", P(0, 0, 1.20), P(0, 0, 1.50), "Spine"),
    ("Neck", P(0, 0, 1.50), P(0, 0, 1.60), "Chest"),
    ("Head", P(0, 0, 1.60), P(0, 0, 1.86), "Neck"),
    ("Scarf", P(0, -0.10, 1.53), P(0, -0.16, 1.22), "Chest"),
    ("Sash", P(0, -0.11, 0.99), P(0, -0.14, 0.58), "Hips"),
]
for side, s in (("R", 1), ("L", -1)):
    BONES += [
        (f"UpperArm.{side}", P(s * SHOULDER_X, 0, 1.47), P(s * SHOULDER_X, 0, 1.17), "Chest"),
        (f"Forearm.{side}", P(s * SHOULDER_X, 0, 1.17), P(s * SHOULDER_X, 0, 0.92), f"UpperArm.{side}"),
        (f"Hand.{side}", P(s * SHOULDER_X, 0, 0.92), P(s * SHOULDER_X, 0, 0.78), f"Forearm.{side}"),
        (f"Thigh.{side}", P(s * HIP_X, 0, 0.92), P(s * HIP_X, 0, 0.51), "Hips"),
        (f"Shin.{side}", P(s * HIP_X, 0, 0.51), P(s * HIP_X, 0, 0.11), f"Thigh.{side}"),
        (f"Foot.{side}", P(s * HIP_X, 0, 0.11), P(s * HIP_X, 0.16, 0.04), f"Shin.{side}"),
    ]
BONE_NAMES = [b[0] for b in BONES]


# ---------- построение меша ----------

class Builder:
    def __init__(self):
        self.bm = bmesh.new()
        self.deform = self.bm.verts.layers.deform.verify()
        self.color = self.bm.loops.layers.float_color.new("Col")

    def _finish(self, verts, bone, rgb, alpha, sharp=False):
        group = BONE_NAMES.index(bone)
        vs = set(verts)
        faces = {f for v in vs for f in v.link_faces}
        for v in vs:
            v[self.deform][group] = 1.0
        col = (*_lin(rgb), alpha)
        for f in faces:
            f.smooth = not sharp
            for loop in f.loops:
                loop[self.color] = col
        return vs

    def box(self, bone, center, size, rgb, alpha=A_BASE, bevel=0.0, rot=None, taper_top=1.0):
        """Скруглённый параллелепипед. size — (ширина по xr, глубина по yf, высота). taper_top — масштаб верха по X."""
        m = Matrix.Translation(center) @ (rot or Matrix.Identity(4))
        before = set(self.bm.verts)
        res = bmesh.ops.create_cube(self.bm, size=1.0)
        verts = res["verts"]
        for v in verts:
            k = taper_top if v.co.z > 0 else 1.0
            v.co = Vector((v.co.x * size[0] * k, v.co.y * size[1], v.co.z * size[2]))
        if bevel > 0:
            edges = list({e for v in verts for e in v.link_edges})
            bmesh.ops.bevel(self.bm, geom=edges + list(verts), offset=bevel, segments=2 if bevel >= 0.03 else 1, affect='EDGES', profile=0.5)
            verts = [v for v in self.bm.verts if v not in before]
        for v in verts:
            v.co = m @ v.co
        return self._finish(verts, bone, rgb, alpha, sharp=bevel == 0)

    def cyl(self, bone, bottom, top, r_bottom, r_top, rgb, alpha=A_BASE, seg=10, scale_y=1.0, sharp=False):
        """Усечённый конус от точки bottom до top."""
        axis = top - bottom
        depth = axis.length
        rot = axis.normalized().to_track_quat('Z', 'Y').to_matrix().to_4x4()
        m = Matrix.Translation((bottom + top) * 0.5) @ rot @ Matrix.Diagonal((1.0, scale_y, 1.0, 1.0))
        res = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=seg,
                                    radius1=r_bottom, radius2=r_top, depth=depth, matrix=m)
        return self._finish(res["verts"], bone, rgb, alpha, sharp)

    def sphere(self, bone, center, radius, rgb, alpha=A_BASE, scale=(1, 1, 1), u=12, v=8, sharp=False):
        m = Matrix.Translation(center) @ Matrix.Diagonal((scale[0], scale[1], scale[2], 1.0))
        res = bmesh.ops.create_uvsphere(self.bm, u_segments=u, v_segments=v, radius=radius, matrix=m)
        return self._finish(res["verts"], bone, rgb, alpha, sharp)


def _lin(c):
    # Цвета вершин храним линейными (FBX экспортирует их как sRGB при colors_type='SRGB').
    def f(x):
        return x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4
    return tuple(f(x) for x in c)


def build_body(b):
    # --- ноги ---
    for s, side in ((1, "R"), (-1, "L")):
        x = s * HIP_X
        # ботинок: носок вперёд, толстая подошва
        b.box(f"Foot.{side}", P(x, 0.045, 0.06), (0.135, 0.27, 0.12), BOOT, bevel=0.03)
        b.box(f"Foot.{side}", P(x, 0.05, 0.012), (0.145, 0.285, 0.025), METAL_DARK)
        b.cyl(f"Shin.{side}", P(x, 0, 0.10), P(x, 0, 0.20), 0.078, 0.082, BOOT, seg=10)
        # голень в обмотках
        b.cyl(f"Shin.{side}", P(x, 0, 0.18), P(x, 0, 0.50), 0.062, 0.078, WRAP, seg=10)
        b.cyl(f"Shin.{side}", P(x, 0, 0.30), P(x, 0, 0.33), 0.074, 0.074, LEATHER, seg=10)
        # колено — броня
        b.sphere(f"Shin.{side}", P(x, 0.025, 0.51), 0.075, METAL, u=10, v=6)
        b.box(f"Shin.{side}", P(x, 0.07, 0.47), (0.09, 0.04, 0.10), METAL_DARK, bevel=0.012)
        # бедро
        b.cyl(f"Thigh.{side}", P(x, 0, 0.52), P(x, 0, 0.92), 0.074, 0.098, SUIT, seg=10)

    # --- таз, пояс, полы ---
    b.box("Hips", P(0, 0, 0.93), (0.34, 0.22, 0.20), SUIT, bevel=0.05)
    b.cyl("Hips", P(0, 0, 0.97), P(0, 0, 1.04), 0.185, 0.185, TEAM, A_TEAM, seg=14, scale_y=0.68)
    b.box("Hips", P(0, 0.125, 1.005), (0.075, 0.03, 0.055), GLOW, A_GLOW, bevel=0.01)
    b.box("Hips", P(0, 0.115, 0.82), (0.16, 0.025, 0.27), TEAM, A_TEAM, bevel=0.008,
          rot=Matrix.Rotation(math.radians(-6), 4, 'X'))
    # задние полы пояса — на кости Sash (вторичное движение)
    for s in (1, -1):
        b.box("Sash", P(s * 0.06, -0.125, 0.78), (0.085, 0.022, 0.40), TEAM, A_TEAM, bevel=0.008,
              rot=Matrix.Rotation(math.radians(s * 4), 4, 'Y'))

    # --- торс ---
    b.box("Spine", P(0, 0, 1.12), (0.30, 0.20, 0.20), SUIT, bevel=0.05)
    b.box("Chest", P(0, 0, 1.35), (0.33, 0.24, 0.30), SUIT_LIGHT, bevel=0.05, taper_top=1.38)
    # перевязь через грудь (цвет команды)
    b.box("Chest", P(0, 0.005, 1.35), (0.06, 0.27, 0.46), TEAM, A_TEAM, bevel=0.01,
          rot=Matrix.Rotation(math.radians(-38), 4, 'Y'))
    # ядро на груди — источник Сверхновой
    b.box("Chest", P(0, 0.13, 1.39), (0.11, 0.05, 0.11), METAL_DARK, bevel=0.015,
          rot=Matrix.Rotation(math.radians(45), 4, 'Y'))
    b.sphere("Chest", P(0, 0.155, 1.39), 0.045, GLOW, A_GLOW, scale=(1, 0.6, 1), u=8, v=6)
    # спина: пластина-«батарея»
    b.box("Chest", P(0, -0.14, 1.38), (0.22, 0.06, 0.24), METAL_DARK, bevel=0.02)
    b.box("Chest", P(0, -0.172, 1.38), (0.03, 0.012, 0.18), GLOW, A_GLOW)

    # --- шея, воротник, шарф ---
    b.cyl("Neck", P(0, 0, 1.49), P(0, 0, 1.62), 0.055, 0.05, SUIT, seg=8)
    b.cyl("Chest", P(0, -0.01, 1.50), P(0, -0.015, 1.59), 0.15, 0.125, TEAM, A_TEAM, seg=14)
    for s in (1, -1):
        b.box("Scarf", P(s * 0.05, -0.15, 1.36), (0.075, 0.02, 0.34), TEAM, A_TEAM, bevel=0.008,
              rot=Matrix.Rotation(math.radians(-8), 4, 'X') @ Matrix.Rotation(math.radians(s * 6), 4, 'Y'))

    # --- голова: тёмное лицо в капюшоне + светящиеся глаза ---
    b.sphere("Head", P(0, 0.01, 1.71), 0.125, FACE, u=12, v=8)
    for s in (1, -1):
        b.box("Head", P(s * 0.047, 0.118, 1.715), (0.042, 0.02, 0.016), GLOW, A_GLOW,
              rot=Matrix.Rotation(math.radians(s * 12), 4, 'Y'))
    hood = b.sphere("Head", P(0, -0.015, 1.73), 0.165, TEAM, A_TEAM, u=14, v=10)
    _cut_hood(b, hood)
    # маска-забрало под глазами
    b.box("Head", P(0, 0.105, 1.655), (0.17, 0.05, 0.06), METAL_DARK, bevel=0.015)

    # --- руки ---
    for s, side in ((1, "R"), (-1, "L")):
        x = s * SHOULDER_X
        b.sphere(f"UpperArm.{side}", P(x, 0, 1.455), 0.082, SUIT_LIGHT, u=10, v=7)
        b.cyl(f"UpperArm.{side}", P(x, 0, 1.17), P(x, 0, 1.45), 0.064, 0.074, SUIT, seg=10)
        b.sphere(f"Forearm.{side}", P(x, 0, 1.17), 0.06, METAL_DARK, u=10, v=6)
        # массивная перчатка: расширяется к кулаку
        b.cyl(f"Forearm.{side}", P(x, 0, 0.93), P(x, 0, 1.16), 0.115, 0.078, METAL, seg=12)
        b.cyl(f"Forearm.{side}", P(x, 0, 0.962), P(x, 0, 0.99), 0.121, 0.117, GLOW, A_GLOW, seg=12)
        b.cyl(f"Forearm.{side}", P(x, 0, 1.06), P(x, 0, 1.076), 0.101, 0.098, GLOW, A_GLOW, seg=12)
        # гребень снаружи перчатки — читается сверху, показывает, какая рука бьёт
        b.box(f"Forearm.{side}", P(x + s * 0.105, 0, 1.03), (0.035, 0.08, 0.19), METAL_DARK, bevel=0.01)
        # кулак и светящиеся костяшки (торец кулака — им бьёт прямой удар)
        b.box(f"Hand.{side}", P(x, 0.005, 0.855), (0.15, 0.15, 0.13), METAL_DARK, bevel=0.03)
        b.box(f"Hand.{side}", P(x, 0.01, 0.787), (0.13, 0.13, 0.02), GLOW, A_GLOW, bevel=0.006)
        b.box(f"Hand.{side}", P(x, 0.082, 0.855), (0.12, 0.02, 0.075), GLOW, A_GLOW, bevel=0.006)

    # наплечник только слева — асимметрия помогает читать, куда повёрнут боец
    b.sphere("UpperArm.L", P(-SHOULDER_X - 0.02, 0, 1.50), 0.135, METAL, scale=(1.0, 1.15, 0.6), u=12, v=7)
    b.box("UpperArm.L", P(-SHOULDER_X - 0.03, 0, 1.545), (0.12, 0.24, 0.02), GLOW, A_GLOW,
          rot=Matrix.Rotation(math.radians(-14), 4, 'Y'))


def _cut_hood(b, verts):
    """Вырезает лицевой проём капюшона и вытягивает назад острый кончик."""
    center = P(0, -0.015, 1.73)
    faces = {f for v in verts for f in v.link_faces}
    cut = []
    for f in faces:
        c = f.calc_center_median() - center
        fwd = -c.y  # вперёд персонажа — это -Y
        if fwd > 0.07 and -0.12 < c.z < 0.10 and abs(c.x) < 0.13:
            cut.append(f)
    bmesh.ops.delete(b.bm, geom=cut, context='FACES_ONLY')
    for v in verts:
        if not v.is_valid:
            continue
        c = v.co - center
        back = c.y  # назад персонажа — +Y
        if back > 0.05 and c.z > 0.0:
            k = (back / 0.165) * (c.z / 0.165)
            v.co.y += 0.09 * k
            v.co.z += 0.05 * k
        if c.z < -0.08:  # низ капюшона ложится на плечи шире
            v.co.x *= 1.12
            v.co.y = center.y + c.y * 1.08


def build():
    # Сцена: убираем только то, что строим сами (и стартовый куб пустой сцены).
    for name in ("Hero", "HeroRig"):
        ob = bpy.data.objects.get(name)
        if ob:
            bpy.data.objects.remove(ob, do_unlink=True)
    cube = bpy.data.objects.get("Cube")
    if cube and cube.type == 'MESH' and len(cube.data.vertices) == 8:
        bpy.data.objects.remove(cube, do_unlink=True)
    for m in list(bpy.data.meshes):
        if m.users == 0:
            bpy.data.meshes.remove(m)
    for a in list(bpy.data.armatures):
        if a.users == 0:
            bpy.data.armatures.remove(a)

    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0

    # --- скелет ---
    arm_data = bpy.data.armatures.new("HeroRig")
    rig = bpy.data.objects.new("HeroRig", arm_data)
    scene.collection.objects.link(rig)
    _edit_bones(rig)

    # --- меш ---
    mesh = bpy.data.meshes.new("Hero")
    ob = bpy.data.objects.new("Hero", mesh)
    scene.collection.objects.link(ob)
    for name in BONE_NAMES:
        ob.vertex_groups.new(name=name)
    b = Builder()
    build_body(b)
    bmesh.ops.remove_doubles(b.bm, verts=b.bm.verts, dist=0.0005)
    b.bm.to_mesh(mesh)
    b.bm.free()
    mesh.color_attributes.active_color_name = "Col"
    mesh.color_attributes.render_color_index = 0
    mesh.set_sharp_from_angle(angle=math.radians(50))

    ob.parent = rig
    mod = ob.modifiers.new("Armature", 'ARMATURE')
    mod.object = rig

    # Материал — один; в Unity его заменит Game/Character.
    mat = bpy.data.materials.get("M_Hero") or bpy.data.materials.new("M_Hero")
    mesh.materials.clear()
    mesh.materials.append(mat)
    _viewport_material(mat)

    tris = sum(len(p.vertices) - 2 for p in mesh.polygons)
    return ob, rig, tris


def _edit_bones(rig):
    view_layer = bpy.context.view_layer
    prev = view_layer.objects.active
    view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    eb = rig.data.edit_bones
    for name, head, tail, parent in BONES:
        bone = eb.new(name)
        bone.head = head
        bone.tail = tail
        bone.roll = 0.0
        if parent:
            bone.parent = eb[parent]
            bone.use_connect = False
    bpy.ops.object.mode_set(mode='OBJECT')
    view_layer.objects.active = prev


def _viewport_material(mat):
    """Чтобы в Blender было видно цвета вершин (на экспорт не влияет)."""
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    attr = next((n for n in nt.nodes if n.type == "VERTEX_COLOR"), None) or nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Col"
    nt.links.new(attr.outputs[0], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.8


def export(ob, rig):
    os.makedirs(os.path.dirname(OUT_FBX), exist_ok=True)
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    ob.select_set(True)
    rig.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=OUT_FBX,
        use_selection=True,
        object_types={'ARMATURE', 'MESH'},
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',
        axis_up='Y',
        use_mesh_modifiers=False,
        mesh_smooth_type='OFF',
        colors_type='SRGB',
        add_leaf_bones=False,
        primary_bone_axis='Y',
        secondary_bone_axis='X',
        use_armature_deform_only=False,
        bake_anim=False,
        path_mode='STRIP',
    )


ob, rig, tris = build()
export(ob, rig)
try:
    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND, copy=True)
except Exception as e:  # noqa: BLE001 — сохранение исходника не обязательно
    print("blend not saved:", e)
print(f"Hero: {tris} tris, {len(ob.data.vertices)} verts, {len(BONE_NAMES)} bones -> {OUT_FBX}")
