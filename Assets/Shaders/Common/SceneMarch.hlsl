#ifndef FRACTALVISIO_SCENE_MARCH_INCLUDED
#define FRACTALVISIO_SCENE_MARCH_INCLUDED

// The ray march, light and colour shared by every 3D scene. Included after the scene's own
//   float SceneMap(float3 p, out float4 trap, inout float stepScale)
// whose trap carries what the palette can colour by: x the orbit's least radius, y its least
// distance to the coordinate planes, z the iteration it ended at, w its radius at the end.
// stepScale is how much shorter than the estimate a step must be where the estimate runs high.
//
// A line-by-line port of the WPF version's Fractal3DShader (PSMain and its helpers), minus what
// the touch version does elsewhere: the point under the finger is found on the CPU
// (ScenePicker), and the view scale that shrinks haze and occlusion as the camera closes in is
// computed once per frame on the CPU too (_View.x) rather than twice per pixel.

float3 SceneNormal(float3 p, float epsilon)
{
    float2 k = float2(1.0, -1.0);
    float4 trap;
    float s = 1.0;
    return normalize(
        k.xyy * SceneMap(p + k.xyy * epsilon, trap, s) +
        k.yyx * SceneMap(p + k.yyx * epsilon, trap, s) +
        k.yxy * SceneMap(p + k.yxy * epsilon, trap, s) +
        k.xxx * SceneMap(p + k.xxx * epsilon, trap, s));
}

float SceneSoftShadow(float3 origin, float3 direction, float minDistance, float maxDistance, float sharpness)
{
    float result = 1.0;
    float travelled = minDistance;
    float4 trap;
    [loop]
    for (int i = 0; i < 48; i++)
    {
        float s = 1.0;
        float stepDistance = SceneMap(origin + direction * travelled, trap, s);
        result = min(result, sharpness * stepDistance / travelled);
        travelled += clamp(stepDistance, minDistance, 0.25);
        if (result < 0.004 || travelled > maxDistance)
        {
            break;
        }
    }

    return saturate(result);
}

// unit: the scene's scale around the point - 1 in the default view, less closer in. Otherwise the
// offsets, in world units, would cover many small details at once and the shading would blot.
float SceneOcclusion(float3 p, float3 normal, float unit)
{
    float occluded = 0.0;
    float weight = 1.0;
    float4 trap;
    [unroll]
    for (int i = 0; i < 5; i++)
    {
        float s = 1.0;
        float offset = (0.01 + 0.12 * float(i) / 4.0) * unit;
        occluded += (offset - SceneMap(p + normal * offset, trap, s)) / unit * weight;
        weight *= 0.75;
    }

    return saturate(1.0 - 3.0 * occluded);
}

// Haze and depth colouring span: the trace distance, shrunk as the view closes in - more slowly
// than the view, as its square root: inside a cavity the nearest wall is much nearer than what is
// seen past it, and shrinking in proportion drowned everything but that wall in haze.
float SceneDepthSpan()
{
    return max(_March.z * sqrt(_View.x), 1e-6);
}

// Surface colour before light. Every source is brought to about unit size, so the palette's
// scale and offset mean roughly the same whatever it colours by.
float3 SceneAlbedo(float3 normal, float4 trap, float travelled, float3 surfacePoint,
                   float3 rayDirection, float occlusion, float stepsRatio, float depthSpan)
{
    int mode = (int)_Look.x;
    if (mode == 0) return _Surface.rgb;
    if (mode == 1) return abs(normal);

    float value;
    if (mode == 2) value = trap.x;
    else if (mode == 3) value = travelled / depthSpan * 6.0;
    else if (mode == 4) value = trap.y * 3.0;
    else if (mode == 5) value = trap.z / max(_SceneIterations - 1.0, 1.0);
    else if (mode == 6) value = log(1.0 + trap.w) * 0.5;
    else if (mode == 7) value = surfacePoint.y;
    else if (mode == 8) value = 1.0 - occlusion;
    else if (mode == 9) value = 1.0 - saturate(dot(normal, -rayDirection));
    else value = stepsRatio;

    return ScenePalette(value * _ColorMap.x + _ColorMap.y);
}

// One more ray: the reflection of the sky and of the shape's other parts, without recursion.
float3 SceneMirror(float3 surfacePoint, float3 normal, float3 incoming,
                   float epsilon, float pixelRadius, float depthSpan)
{
    float3 reflected = normalize(reflect(incoming, normal));
    float offset = max(epsilon * 8.0, 1e-7);
    float3 origin = surfacePoint + normal * offset;
    float distance = 0.0;
    float4 reflectedTrap = 0.0;
    int steps = 0;
    bool hit = false;
    int limit = min((int)_March.x, 128);
    [loop]
    for (int i = 0; i < limit; i++)
    {
        float4 stepTrap;
        float stepScale = 1.0;
        float separation = SceneMap(origin + reflected * distance, stepTrap, stepScale);
        float tolerance = max(pixelRadius * (distance + offset), epsilon);
        steps = i + 1;
        if (separation < tolerance)
        {
            hit = true;
            reflectedTrap = stepTrap;
            break;
        }

        distance += max(separation * stepScale, tolerance);
        if (distance > _March.z)
        {
            break;
        }
    }

    if (!hit)
    {
        // A ray off into the sky sees the lamp too, as metal does: the ray itself has checked
        // the shape does not hide it. Without it the mirrored shape sank to the background.
        float sheen = saturate(dot(reflected, _LightDir.xyz));
        return SceneSky(reflected) * 2.0 + _LightColor.rgb * (pow(sheen, 128.0) * 5.0 + pow(sheen, 6.0) * 0.45);
    }

    float3 hitPoint = origin + reflected * distance;
    float3 reflectedNormal = SceneNormal(hitPoint, max(epsilon, 1e-6));
    float stepsRatio = saturate((float)steps / max(_March.x, 1.0));
    float3 albedo = SceneAlbedo(reflectedNormal, reflectedTrap, distance, hitPoint, reflected, 1.0, stepsRatio, depthSpan);
    float diffuse = saturate(dot(reflectedNormal, _LightDir.xyz));
    float3 lit = albedo * (_Look.w * 0.7 + diffuse * _LightColor.rgb);
    return lerp(lit, SceneSky(reflected), saturate(distance / depthSpan));
}

half4 Frag(Varyings input) : SV_Target
{
    // Rows outside the band being marched keep the frame drawn so far.
    float2 local = input.uv * _Window.zw;
    if (local.y < _Band.x || local.y >= _Band.y)
    {
        return SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, input.uv, 0);
    }

    // The whole image is exactly two units high at the field-of-view scale ahead of the camera,
    // so the scale gives the field of view asked for.
    float2 pixel = _Window.xy + local;
    float2 plane = (pixel - 0.5 * _Frame.xy) / (0.5 * _Frame.y);

    float3 rayDirection = normalize(
        _CameraForward.xyz * _CameraPosition.w + _CameraRight.xyz * plane.x + _CameraUp.xyz * plane.y);
    float3 rayOrigin = _CameraPosition.xyz;

    float pixelRadius = 2.0 * _March.y / max(_Frame.y * _CameraPosition.w, 1.0);
    int maxSteps = (int)_March.x;
    float maxDistance = _March.z;

    // Start at the shape's bounding sphere: far from it the estimate can overshoot the front
    // surface, and a fixed range would cut it off. The allowance is for vertices lying on the
    // sphere, which a ray counts as hit a fraction of a pixel early.
    float radius = _March.w;
    float bound = radius * 1.001 + 2.0 * pixelRadius * (length(rayOrigin) + radius);
    float closest = -dot(rayOrigin, rayDirection);
    float3 closestPoint = rayOrigin + rayDirection * closest;
    float distanceSquared = dot(closestPoint, closestPoint);
    if (distanceSquared > bound * bound)
    {
        return half4(SceneLinearToSrgb(SceneSky(rayDirection)), 1.0);
    }

    float halfChord = sqrt(max(bound * bound - distanceSquared, 0.0));
    if (closest + halfChord < 0.0)
    {
        return half4(SceneLinearToSrgb(SceneSky(rayDirection)), 1.0);
    }

    float entryDistance = max(0.0, closest - halfChord);

    // Haze and depth colour are measured from the camera while it is within three radii; past
    // that the origin rides along with it, so backing away neither drowns the shape in haze nor
    // shifts it along the palette.
    float shadeOrigin = max(0.0, length(rayOrigin) - 3.0 * radius);
    maxDistance = max(maxDistance, closest + halfChord);

    int style = (int)_Style.x;
    float strength = max(_Style.y, 0.0);
    bool volumetric = style == 3 || style == 4;
    bool pierce = style == 4;

    float travelled = entryDistance;
    float4 trap = 0.0;
    float epsilon = 1e-6;
    float proximity = 0.0;
    int usedSteps = 0;
    bool hit = false;

    // The ray's nearest pass to the surface, in pixels: if the steps run out before it lands or
    // leaves, that point is taken rather than the background.
    float bestRatio = 1e20;
    float bestTravelled = travelled;
    float4 bestTrap = 0.0;
    bool exhausted = true;

    [loop]
    for (int i = 0; i < maxSteps; i++)
    {
        float4 stepTrap;
        float stepScale = 1.0;
        float stepDistance = SceneMap(rayOrigin + rayDirection * travelled, stepTrap, stepScale);
        epsilon = max(pixelRadius * travelled, 1e-7);
        usedSteps = i + 1;
        float ratio = stepDistance / epsilon;
        if (ratio < bestRatio)
        {
            bestRatio = ratio;
            bestTravelled = travelled;
            bestTrap = stepTrap;
        }

        // In the second half of the budget the margin is let go gradually: otherwise a ray
        // creeping along the surface near a pole spends every step and leaves a black seam.
        float relax = saturate(2.0 * (float)i / max((float)maxSteps, 1.0) - 1.0);
        float safeStep = max(stepDistance * lerp(stepScale, 1.0, relax), epsilon);
        float advance = stepDistance < epsilon ? max(epsilon * 2.0, stepDistance) : safeStep;

        // Closeness adds up weighted by the way travelled, not by steps: a ray stuck at the
        // surface would otherwise saturate to white within a frame.
        if (volumetric)
        {
            proximity += exp(-abs(stepDistance) * 22.0) * advance;
        }

        if (stepDistance < epsilon && !pierce)
        {
            trap = stepTrap;
            hit = true;
            break;
        }

        travelled += advance;
        if (travelled > maxDistance)
        {
            exhausted = false;
            break;
        }
    }

    // A grazing ray, or one in a narrow gap, spends every step at the surface. The background in
    // its place would be a black hole, so a ray that came within a few pixels counts as a hit at
    // its nearest point (enhanced sphere tracing).
    if (!hit && !pierce && exhausted && bestRatio < 16.0)
    {
        hit = true;
        travelled = bestTravelled;
        trap = bestTrap;
    }

    float3 sky = SceneSky(rayDirection);
    float stepsRatio = saturate((float)usedSteps / max((float)maxSteps, 1.0));

    // Haze, depth and gathered closeness are measured against the set trace distance, not the
    // ray's working limit: that grows as the camera backs away, and the shape would change
    // colour with distance alone.
    float traceSpan = max(_March.z, 1e-3);
    float shadeDepth = travelled - shadeOrigin;

    if (style == 4)
    {
        // Against the trace distance, or a large shape would saturate to a solid blot.
        float density = saturate(proximity / traceSpan * 12.0 * max(strength, 1e-3));
        float3 tint = ScenePalette(density * _ColorMap.x + _ColorMap.y);
        return half4(SceneLinearToSrgb(lerp(sky, tint, density)), 1.0);
    }

    float glow = style == 3 ? saturate(proximity / traceSpan * 10.0 * strength) : 0.0;
    float3 glowTint = style == 3 ? ScenePalette(glow * _ColorMap.x + _ColorMap.y) : 0.0;

    if (!hit)
    {
        return half4(SceneLinearToSrgb(sky + glowTint * glow), 1.0);
    }

    float3 surfacePoint = rayOrigin + rayDirection * travelled;
    float3 normal = SceneNormal(surfacePoint, max(epsilon, 1e-6));
    float3 lightDirection = _LightDir.xyz;
    float3 lightTint = _LightColor.rgb;

    float diffuse = saturate(dot(normal, lightDirection));
    float shadow = 1.0;
    if (_Look.y > 0.0 && diffuse > 0.0)
    {
        shadow = SceneSoftShadow(surfacePoint + normal * epsilon * 4.0, lightDirection,
            max(epsilon * 8.0, 1e-5), maxDistance * 0.5, _Look.y);
    }

    float viewScale = clamp(_View.x, 1e-6, 1.0);
    float depthSpan = SceneDepthSpan();
    float occlusion = 1.0;
    if (_Look.z > 0.5)
    {
        // A point far from the camera, larger than a pixel, does not shade finer than one: the
        // offsets are held between a pixel and the default view's scale.
        float occlusionUnit = min(max(viewScale, epsilon * 20.0), 1.0);
        occlusion = lerp(1.0, SceneOcclusion(surfacePoint, normal, occlusionUnit), saturate(_Style.w));
    }

    // The ambient light is neutral or takes the colour of the sky above the point.
    float3 ambientTint = lerp(float3(1.0, 1.0, 1.0), SceneSky(normal) * 3.0, saturate(_Style.z));
    float3 ambient = _Look.w * ambientTint;
    float3 albedo = SceneAlbedo(normal, trap, shadeDepth, surfacePoint, rayDirection, occlusion, stepsRatio, depthSpan);
    float3 halfVector = normalize(lightDirection - rayDirection);
    float specular = _LightDir.w * pow(saturate(dot(normal, halfVector)), 32.0) * shadow;

    float3 color;
    if (style == 1)
    {
        // Clay: light wrapped round the shape, no highlight, folds deeper.
        float wrapped = saturate(dot(normal, lightDirection) * 0.5 + 0.5);
        color = albedo * (ambient * 0.8 + wrapped * shadow * lightTint) * occlusion * occlusion;
    }
    else if (style == 2)
    {
        // Metal: the sky reflected in the surface, a hard highlight from the lamp on top.
        float3 reflected = reflect(rayDirection, normal);
        float fresnel = pow(1.0 - saturate(dot(normal, -rayDirection)), 5.0);
        float sheen = saturate(dot(reflected, lightDirection));
        float3 environment = SceneSky(reflected) * 2.0 +
            lightTint * (pow(sheen, 128.0) * 5.0 + pow(sheen, 6.0) * 0.45);
        color = albedo * (ambient + diffuse * shadow * lightTint * 0.25) * occlusion * 0.6 +
            albedo * environment * lerp(0.35, 1.0, fresnel) * shadow * strength;
    }
    else if (style == 5)
    {
        // Studio: lit from the normal in the camera's axes, so the shape reads the same from
        // every side, whatever the lamp does.
        float2 screenNormal = float2(dot(normal, _CameraRight.xyz), dot(normal, _CameraUp.xyz));
        float key = saturate(dot(screenNormal, normalize(float2(-0.55, 0.62))) * 0.6 + 0.55);
        float fill = saturate(dot(screenNormal, normalize(float2(0.7, -0.3))) * 0.5 + 0.5) * 0.25;
        float rim = pow(saturate(length(screenNormal)), 6.0) * 0.55 * strength;
        color = albedo * (key + fill + _Look.w * 0.3) * occlusion + rim * lightTint;
    }
    else if (style == 6)
    {
        // Cartoon: light in steps, the silhouette outlined dark.
        float levels = max(2.0, floor(2.0 + strength * 2.0));
        float lit = floor(saturate(diffuse * shadow) * levels) / max(levels - 1.0, 1.0);
        float edge = 1.0 - smoothstep(0.12, 0.42, saturate(dot(normal, -rayDirection)));
        color = albedo * (ambient + lit * lightTint) * occlusion * (1.0 - edge);
    }
    else if (style == 7)
    {
        // Translucent: light from behind shows the more, the thinner the place.
        float thickness = pow(saturate(occlusion), 3.0);
        float back = pow(saturate(dot(-normal, lightDirection)) * 0.6 + 0.4, 2.0);
        color = albedo * (ambient + diffuse * shadow * lightTint * 0.6) * occlusion +
            albedo * back * thickness * strength * lightTint * 2.5 + specular * lightTint;
    }
    else if (style == 8)
    {
        float3 reflected = SceneMirror(surfacePoint, normal, rayDirection, epsilon, pixelRadius, depthSpan);
        float3 baseColor = albedo * (ambient + diffuse * shadow * lightTint) * occlusion + specular * lightTint;
        color = lerp(baseColor, reflected * albedo, saturate(strength));
    }
    else
    {
        color = albedo * (ambient + diffuse * shadow * lightTint) * occlusion + specular * lightTint;
    }

    if (style == 3)
    {
        color += glowTint * glow * 1.2;
    }

    color = lerp(color, sky, saturate(shadeDepth / depthSpan));
    return half4(SceneLinearToSrgb(color), 1.0);
}

#endif
