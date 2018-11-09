using System.Runtime.CompilerServices;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace OSMTrafficSim.BVH
{
    //https://devblogs.nvidia.com/thinking-parallel-part-iii-tree-construction-gpu/
    [BurstCompile]
    struct ConstructBVHInternalNodesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<uint> mortonCodes;

        [WriteOnly] public NativeArray<BVHNode> BVHArray;

        [NativeDisableParallelForRestriction] public NativeArray<int> ParentIndex;

        public int NumObjects;

        int2 determineRange(int i)
        {
            int d = (clz_safe(i, i + 1) - clz_safe(i, i - 1)) > 0 ? 1 : -1;
            int commonPrefixMin = clz_safe(i, i - d);
            int l_max = 2;
            while (clz_safe(i, i + d * l_max) > commonPrefixMin)
            {
                l_max *= 2;
            }

            int l = 0;
            int t = l_max;
            do
            {
                t = (t + 1) >> 1; // exponential decrease
                if (clz_safe(i, i + d * (l + t)) > commonPrefixMin)
                {
                    l += t;
                }
            } while (t > 1);

            int j = i + l * d;
            int2 range = d > 0 ? new int2(i, j) : new int2(j, i);
            return range;
        }

        int findSplit(int first, int last)
        {
            // Calculate the number of highest bits that are the same
            // for all objects, using the count-leading-zeros intrinsic.

            int commonPrefix = clz_index(first, last);

            // Use binary search to find where the next bit differs.
            // Specifically, we are looking for the highest object that
            // shares more than commonPrefix bits with the first one.

            int split = first; // initial guess
            int step = last - first;

            do
            {
                step = (step + 1) >> 1; // exponential decrease
                int newSplit = split + step; // proposed new position

                if (newSplit < last)
                {
                    int splitPrefix = clz_index(first, newSplit);
                    if (splitPrefix > commonPrefix)
                        split = newSplit; // accept proposal
                }
            } while (step > 1);

            return split;
        }

        private int clz_safe(int idx, int idy)
        {
            if (idy < 0 || idy > NumObjects - 1) return -1;
            return clz_index(idx, idy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int clz_index(int idx, int idy)
        {
            //rely on morton being unique, otherwise clz its index
            //https://devblogs.nvidia.com/parallelforall/wp-content/uploads/2012/11/karras2012hpg_paper.pdf
            //see section 4. BVHs, Octrees, and k-d Trees
            return mortonCodes[idx] == mortonCodes[idy]
                ? (NumObjects - math.max(idx, idy)) + 32
                : clz_value(mortonCodes[idx], mortonCodes[idy]);
        }

        //https://stackoverflow.com/questions/10439242/count-leading-zeroes-in-an-int32
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int clz_value(uint value1, uint value2)
        {
            uint value = value1 ^ value2;
            //do the smearing
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            //count the ones
            value -= ((value >> 1) & 0x55555555);
            value = (((value >> 2) & 0x33333333) + (value & 0x33333333));
            value = (((value >> 4) + value) & 0x0f0f0f0f);
            value += (value >> 8);
            value += (value >> 16);
            return (int)(32 - (value & 0x0000003f));
        }

        public void Execute(int index)
        {
            int halfBVHSize = BVHArray.Length / 2;
            int2 range = determineRange(index);
            int first = range.x;
            int last = range.y;

            // Determine where to split the range.

            int split = findSplit(first, last);

            int childAIndex = split == first ? split + halfBVHSize : split;
            int childBIndex = (split + 1) == last ? split + halfBVHSize + 1 : split + 1;

            BVHNode thisnode = new BVHNode();
            thisnode.RightNodeIndex = childBIndex;
            thisnode.LeftNodeIndex = childAIndex;
            thisnode.EntityId = -1;
            thisnode.IsValid = 0; //No AABB Updated
            BVHArray[index] = thisnode;

            ParentIndex[childAIndex] = index;
            ParentIndex[childBIndex] = index;
        }
    }
}