#include "meshoptimizer.h"
#include <stddef.h>
#if defined(_WIN32)
#define JULY_EXPORT extern "C" __declspec(dllexport)
#else
#define JULY_EXPORT extern "C" __attribute__((visibility("default")))
#endif
JULY_EXPORT size_t july_mesh_simplify(unsigned int* output, const unsigned int* indices, size_t index_count,
    const float* positions, size_t vertex_count, const float* attributes, const float* weights, size_t attribute_count,
    const unsigned char* locks, size_t target_count, float error, float* result_error)
{
    return meshopt_simplifyWithAttributes(output, indices, index_count, positions, vertex_count, sizeof(float) * 3,
        attributes, sizeof(float) * attribute_count, weights, attribute_count, locks, target_count, error,
        meshopt_SimplifyLockBorder, result_error);
}
