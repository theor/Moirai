// class Sequence : IEffect
// {
//     private List<IEffect> Predicates = new();
//     public Sequence(params IEffect[] predicates)
//     {
//         Predicates.AddRange(predicates);
//     }
//
//     public bool MakeTrue(ExecuteContext ctx)
//     {
//         bool res = true;
//         foreach (var predicate in Predicates)
//         {
//             res = res && predicate.MakeTrue(ctx);
//         }
//         return res;
//     }
// }
