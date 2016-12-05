using System.Threading.Tasks;

//ref 
// http://forum.unity3d.com/threads/scale2x-sample-code-and-project.174656/
// http://6bit.net/shonumi/2013/04/10/in-depth-scale2x/
// http://scale2x.sourceforge.net/algorithm.html

namespace ScalexFilter
{
    public unsafe class ScalexTool
    {

        public static void toScale2x_dx(uint* src_fast, int org_width, int org_height, uint* buffer_2x)
        {

            int new_w = org_width * 2;

            Parallel.For(0, org_height, y =>
            {
                for (int x = org_width - 1; x >= 0; --x)
                {

                    uint s_B, s_D, s_E, s_F, s_H, s_E0, s_E1, s_E2, s_E3;
                    int x_dec_1, x_add_1, y_dec_1, y_add_1;

                    x_dec_1 = x - 1;
                    x_add_1 = x + 1;
                    y_dec_1 = y - 1;
                    y_add_1 = y + 1;

                    s_E0 = s_E1 = s_E2 = s_E3 = s_D = s_F = s_B = s_H = s_E = src_fast[y * org_width + x];

                    if (x_dec_1 >= 0) s_D = src_fast[y * org_width + x_dec_1];  // src_fast[x_dec_1][y];
                    if (x_add_1 < org_width) s_F = src_fast[y * org_width + x_add_1]; ;//src_fast[x_add_1][y];
                    if (y_dec_1 >= 0) s_B = src_fast[y_dec_1 * org_width + x]; ;//src_fast[x][y_dec_1];
                    if (y_add_1 < org_height) s_H = src_fast[y_add_1 * org_width + x]; ;//src_fast[x][y_add_1];

                    if ((s_B - s_H) != 0 && (s_D - s_F) != 0)
                    {
                        if (s_D == s_B) s_E0 = s_D;
                        if (s_B == s_F) s_E1 = s_F;
                        if (s_D == s_H) s_E2 = s_D;
                        if (s_H == s_F) s_E3 = s_F;
                    }

                    buffer_2x[((x << 1)) + (y << 1) * new_w] = s_E0;
                    buffer_2x[((x << 1) + 1) + (y << 1) * new_w] = s_E1;
                    buffer_2x[((x << 1)) + ((y << 1) + 1) * new_w] = s_E2;
                    buffer_2x[((x << 1) + 1) + ((y << 1) + 1) * new_w] = s_E3;


                }
            });
        }


        public static void toScale3x_dx(uint* src_fast, int org_width, int org_height, uint* buffer_3x)
        {
            int new_w = org_width * 3;

            Parallel.For(0, org_height, y =>
            {
                for (int x = org_width - 1; x >= 0; --x)
                {

                    uint s_A, s_B, s_C, s_D, s_E, s_F, s_G, s_H, s_I, s_E0, s_E1, s_E2, s_E3, s_E4, s_E5, s_E6, s_E7, s_E8;
                    int x_dec_1, x_add_1, y_dec_1, y_add_1;

                    x_dec_1 = x - 1;
                    x_add_1 = x + 1;
                    y_dec_1 = y - 1;
                    y_add_1 = y + 1;
                    s_A = s_B = s_G = s_I = s_C = s_D = s_H = s_F = s_E0 = s_E1 = s_E2 = s_E3 = s_E4 = s_E5 = s_E6 = s_E7 = s_E8 = s_E = src_fast[y * org_width + x];

                    if (x_dec_1 >= 0)
                    {
                        s_D = src_fast[y * org_width + x_add_1]; // src_fast[x_dec_1][y];
                        if (y_dec_1 >= 0) s_A = src_fast[y_dec_1 * org_width + x_dec_1];  //src_fast[x_dec_1][y_dec_1];
                        if (y_add_1 < org_height) s_G = src_fast[y_add_1 * org_width + x_dec_1];  //src_fast[x_dec_1][y_add_1];
                    }

                    if (x_add_1 < org_width)
                    {
                        s_F = src_fast[y * org_width + x_add_1]; // src_fast[x_add_1][y];
                        if (y_add_1 < org_height) s_I = src_fast[y_add_1 * org_width + x_add_1];  //src_fast[x_add_1][y_add_1];
                        if (y_dec_1 >= 0) s_C = src_fast[y_dec_1 * org_width + x_add_1]; // src_fast[x_add_1][y_dec_1];
                    }

                    if (y_dec_1 >= 0) s_B = src_fast[y_dec_1 * org_width + x];  //src_fast[x][y_dec_1];
                    if (y_add_1 < org_height) s_H = src_fast[y_add_1 * org_width + x]; ;// src_fast[x][y_add_1];


                    if (s_B != s_H && s_D != s_F)
                    {
                        if (s_D == s_B) s_E0 = s_D;
                        if ((s_D == s_B && s_E != s_C) || (s_B == s_F && s_E != s_A)) s_E1 = s_B;
                        if (s_B == s_F) s_E2 = s_F;
                        if ((s_D == s_B && s_E != s_G) || (s_D == s_H && s_E != s_A)) s_E3 = s_D;
                        if ((s_B == s_F && s_E != s_I) || (s_H == s_F && s_E != s_C)) s_E5 = s_F;
                        if (s_D == s_H) s_E6 = s_D;
                        if ((s_D == s_H && s_E != s_I) || (s_H == s_F && s_E != s_G)) s_E7 = s_H;
                        if (s_H == s_F) s_E8 = s_F;
                    }

                    buffer_3x[(x * 3) + (y * 3) * new_w] = s_E0;
                    buffer_3x[(1 + x * 3) + (y * 3) * new_w] = s_E1;
                    buffer_3x[(2 + x * 3) + (y * 3) * new_w] = s_E2;
                    buffer_3x[(x * 3) + (1 + y * 3) * new_w] = s_E3;
                    buffer_3x[(1 + x * 3) + (1 + y * 3) * new_w] = s_E4;
                    buffer_3x[(2 + x * 3) + (1 + y * 3) * new_w] = s_E5;
                    buffer_3x[(x * 3) + (2 + y * 3) * new_w] = s_E6;
                    buffer_3x[(1 + x * 3) + (2 + y * 3) * new_w] = s_E7;
                    buffer_3x[(2 + x * 3) + (2 + y * 3) * new_w] = s_E8;
                }
            });
        }




    }
}
