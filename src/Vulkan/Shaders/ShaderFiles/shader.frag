// HERZLICHEN GLÜCKWUNSCH!
// Du hast erfolgreich historischen Unrat aus Tag 2 der Entwicklung ausgegraben.
// Dieser Shader wird nirgendwo eingebunden, spricht sogar noch altes GLSL 450
// (obwohl die Engine längst auf HLSL läuft) und existiert nur noch als Mahnmal
// für Dinge, die "später noch aufgeräumt werden".
//
// Kostet im Release-Build: 0 Bytes VRAM, 0 Draw-Calls, 0 GC-Allokationen.
// Wenn du das hier liest, bist du tief genug im Code, um selbst Pull Requests zu öffnen.

#version 450

layout(set = 0, binding = 0) uniform sampler2D texSampler;

layout(location = 0) in vec3 fragColor;
layout(location = 1) in vec2 fragTexCoord;

layout(location = 0) out vec4 outColor;

void main() {
    // Multipliziert die Texturfarbe mit der Vertex-Farbe (weiß = reine Textur)
    outColor = vec4(fragColor, 1.0) * texture(texSampler, fragTexCoord);
}