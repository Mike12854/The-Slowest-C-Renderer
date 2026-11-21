// ACHTUNG: UNBENUTZTER SHADER-UNRAT (Part 2 – der dazugehörige Vertex-Shader)
// Falls du dich fragst, wo dieser Shader verwendet wird: Gar nicht.
// Relikt aus der Single-Draw-Steinzeit (mit einer einzelnen mat4 model statt 300k Instanzen).
// Kostet im Release-Build: 0 Bytes VRAM, 0 Draw-Calls, 0 GC-Allokationen.
// Falls du hier irgendwo ein LINQ-Statement findest, sag sofort Bescheid.

#version 450

layout(binding = 0) uniform UniformBufferObject {
    mat4 model;
    mat4 view;
    mat4 proj;
} ubo;

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inColor;
layout(location = 2) in vec2 inTexCoord;

layout(location = 0) out vec3 fragColor;
layout(location = 1) out vec2 fragTexCoord;

void main() {
    gl_Position = ubo.proj * ubo.view * ubo.model * vec4(inPosition, 1.0);
    fragColor = inColor;
    fragTexCoord = inTexCoord;
}