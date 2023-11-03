parser grammar Moirai;
options {
  tokenVocab=moirai_lexer;

}
r: (COMMENT | LINE_BREAK)* (action|event|prop_definition|enum_definition|type_definition)+ EOF;

action: RULE AT? ID SCOPE_OPEN LINE_BREAK effect+ SCOPE_CLOSE LINE_BREAK*;
event: EVENT ID SCOPE_OPEN LINE_BREAK when+ effect+ SCOPE_CLOSE LINE_BREAK*;
when: WHEN (VAR_ID COLON)? expr (COMMA expr)* SPACE* LINE_BREAK+;
effect: (set |  call_assign) SPACE* LINE_BREAK+;

set: SET  path EQ expr;
call_assign : ID (VAR_ID COLON)?  ((expr (COMMA expr)* )) scope?;
call : ID ((expr (COMMA expr)* )) scope?;
scope: SCOPE_OPEN LINE_BREAK* effect* SCOPE_CLOSE LINE_BREAK*;
value: call | string | enum_value | TYPE_ID | path | bool | number | NULL;
expr
    : left=expr op=(EQ | NEQ | GE | LE | GT | LT) right=expr
    | left=expr op=(MUL | DIV) right=expr
    | left=expr op=(ADD | SUB) right=expr
    | (PAREN_OPEN paren_expr=expr PAREN_CLOSE)
    | value
    ;

type_definition: ENTITY TYPE_ID SCOPE_OPEN LINE_BREAK* SCOPE_CLOSE LINE_BREAK+ ;

prop_definition: PROP ID EQ (ID|TYPE_ID) LINE_BREAK+ ;

enum_definition: ENUM TYPE_ID SCOPE_OPEN LINE_BREAK* TYPE_ID (COMMA TYPE_ID)* LINE_BREAK* SCOPE_CLOSE LINE_BREAK+ ;

string: STRING ;

bool: TRUE | FALSE;
path : VAR_ID (DOT ID)* | ID;
enum_value: TYPE_ID DOT TYPE_ID ;
number: NUMBER;