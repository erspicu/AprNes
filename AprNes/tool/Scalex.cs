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

                int x = org_width;

                while (--x > -1)
                //for (int x = org_width - 1; x >= 0; --x)
                {

                    uint s_B, s_D, s_E, s_F, s_H;

                    int x_dec_1 = x - 1;
                    int x_add_1 = x + 1;
                    int y_dec_1 = y - 1;
                    int y_add_1 = y + 1;

                    s_E = src_fast[y * org_width + x];

                    if (x_dec_1 >= 0) s_D = src_fast[y * org_width + x_dec_1]; else s_D = s_E;
                    if (x_add_1 < org_width) s_F = src_fast[y * org_width + x_add_1]; else s_F = s_E;
                    if (y_dec_1 >= 0) s_B = src_fast[y_dec_1 * org_width + x]; else s_B = s_E;
                    if (y_add_1 < org_height) s_H = src_fast[y_add_1 * org_width + x]; else s_H = s_E;

                    if ((s_B ^ s_H) != 0 && (s_D ^ s_F) != 0)
                    {
                        if (s_D == s_B) buffer_2x[((x << 1)) + (y << 1) * new_w] = s_D; else buffer_2x[((x << 1)) + (y << 1) * new_w] = s_E;
                        if (s_B == s_F) buffer_2x[((x << 1) + 1) + (y << 1) * new_w] = s_F; else buffer_2x[((x << 1) + 1) + (y << 1) * new_w] = s_E;
                        if (s_D == s_H) buffer_2x[((x << 1)) + ((y << 1) + 1) * new_w] = s_D; else buffer_2x[((x << 1)) + ((y << 1) + 1) * new_w] = s_E;
                        if (s_H == s_F) buffer_2x[((x << 1) + 1) + ((y << 1) + 1) * new_w] = s_F; else buffer_2x[((x << 1) + 1) + ((y << 1) + 1) * new_w] = s_E;
                    }
                    else
                    {

                        buffer_2x[((x << 1)) + (y << 1) * new_w] = s_E;
                        buffer_2x[((x << 1) + 1) + (y << 1) * new_w] = s_E;
                        buffer_2x[((x << 1)) + ((y << 1) + 1) * new_w] = s_E;
                        buffer_2x[((x << 1) + 1) + ((y << 1) + 1) * new_w] = s_E;
                    }
                }
            });
        }




        public static void toScale3x_dx(uint* src_fast, int org_width, int org_height, uint* buffer_3x)
        {
            int new_w = org_width * 3;

            Parallel.For(0, org_height, y =>
            {

                int x = org_width;

                while (--x > -1)
                //for (int x = org_width - 1; x >= 0; --x)
                {

                    uint s_A, s_B, s_C, s_D, s_E, s_F, s_G, s_H, s_I;
                    int x_dec_1, x_add_1, y_dec_1, y_add_1;

                    x_dec_1 = x - 1;
                    x_add_1 = x + 1;
                    y_dec_1 = y - 1;
                    y_add_1 = y + 1;
                    s_E = src_fast[y * org_width + x];

                    if (x_dec_1 >= 0)
                    {
                        s_D = src_fast[y * org_width + x_add_1];
                        if (y_dec_1 >= 0) s_A = src_fast[y_dec_1 * org_width + x_dec_1]; else s_A = s_E;
                        if (y_add_1 < org_height) s_G = src_fast[y_add_1 * org_width + x_dec_1]; s_G = s_E;
                    }
                    else
                        s_D = s_A = s_G = s_E;



                    if (x_add_1 < org_width)
                    {
                        s_F = src_fast[y * org_width + x_add_1];
                        if (y_add_1 < org_height) s_I = src_fast[y_add_1 * org_width + x_add_1]; else s_I = s_E;
                        if (y_dec_1 >= 0) s_C = src_fast[y_dec_1 * org_width + x_add_1]; else s_C = s_E;
                    }
                    else
                        s_I = s_C = s_F = s_E;

                    s_B = (y_dec_1 >= 0) ? src_fast[y_dec_1 * org_width + x] : s_E;
                    s_H = (y_add_1 < org_height) ? src_fast[y_add_1 * org_width + x] : s_E;

                    int t1 = (2 + x * 3);
                    int t2 = (2 + y * 3) * new_w;
                    int t4 = (x * 3);
                    int t3 = t4 + 1;
                    int t6 = (y * 3) * new_w;
                    int t5 = t6 + new_w;

                    //if ((s_B ^ s_H) != 0 && (s_D ^ s_F) != 0)
                    if (s_B != s_H && s_D != s_F)
                    {
                        if (s_D == s_B) buffer_3x[t4 + t6] = s_D; else buffer_3x[t4 + t6] = s_E;
                        if ((s_D == s_B && s_E != s_C) || (s_B == s_F && s_E != s_A)) buffer_3x[t3 + t6] = s_B; else buffer_3x[t3 + t6] = s_E;
                        if (s_B == s_F) buffer_3x[t1 + t6] = s_F; else buffer_3x[t1 + t6] = s_E;
                        if ((s_D == s_B && s_E != s_G) || (s_D == s_H && s_E != s_A)) buffer_3x[t4 + t5] = s_D; else buffer_3x[t4 + t5] = s_E;
                        if ((s_B == s_F && s_E != s_I) || (s_H == s_F && s_E != s_C)) buffer_3x[t1 + t5] = s_F; else buffer_3x[t1 + t5] = s_E;
                        if (s_D == s_H) buffer_3x[t4 + t2] = s_D; else buffer_3x[t4 + t2] = s_E;
                        if ((s_D == s_H && s_E != s_I) || (s_H == s_F && s_E != s_G)) buffer_3x[t3 + t2] = s_H; else buffer_3x[t3 + t2] = s_E;
                        if (s_H == s_F) buffer_3x[t1 + t2] = s_F; else buffer_3x[t1 + t2] = s_E;
                    }
                    else
                    {
                        buffer_3x[t1 + t2] =  //8
                        buffer_3x[t3 + t2] =  //7
                        buffer_3x[t4 + t2] =  // 6
                        buffer_3x[t1 + t5] = //5
                        buffer_3x[t3 + t5] =//4
                        buffer_3x[t4 + t5] =  //3
                        buffer_3x[t1 + t6] =  // 2
                        buffer_3x[t3 + t6] =  //1
                        buffer_3x[t4 + t6] = s_E; //0
                    }
                }
            });
        }




    }
}
